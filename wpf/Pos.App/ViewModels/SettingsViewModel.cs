using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.App.Services;
using Pos.Core.Data;
using Pos.Core.Models;
using Pos.Core.Repositories;

namespace Pos.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private const string SettingsKey = "pos_wpf_settings";

    /// <summary>Settings that belong to this machine, not the business — never synced.</summary>
    private const string DeviceSettingsKey = "pos_wpf_device_settings";

    // The keys the rest of the system already uses for these, so a change made here shows up
    // on the dashboard and the mobile menu instead of only inside this app.
    private const string ProfileKey = "restaurant_profile";
    private const string UpiKey = "upi_settings";
    private const string DailyResetKey = "daily_reset_bill_counter";

    private static string Pick(string? preferred, string? fallback, string current)
        => !string.IsNullOrWhiteSpace(preferred) ? preferred
         : !string.IsNullOrWhiteSpace(fallback) ? fallback
         : current;
    private readonly AppSettingsRepository _settings;
    private readonly CatalogRepository _catalog;

    [ObservableProperty] private string _activeTab = "profile";        // profile|printer|shortcut|defaults|menu
    [ObservableProperty] private string _activeSubTab = "category";    // category|table|gst

    // Profile. Every field starts empty on purpose: these print on the bill, and a built-in
    // default is one brand's real details shown under another brand's name — a till that has
    // never synced would print Chay Chaupal's GST number for whoever is billing on it.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StoreInitials))]
    private string _storeName = "";
    [ObservableProperty] private string _storeWebsite = "";
    [ObservableProperty] private string _storePhone = "";
    [ObservableProperty] private string _storeEmail = "";
    [ObservableProperty] private string _storeGstNo = "";
    [ObservableProperty] private string _storeFoodLicenseNo = "";
    [ObservableProperty] private string _storeAddress = "";
    [ObservableProperty] private string _storeLogoUrl = "";
    [ObservableProperty] private bool _showNameOnBill = true;
    [ObservableProperty] private bool _showWebsiteOnBill = false;
    [ObservableProperty] private bool _showPhoneOnBill = true;
    [ObservableProperty] private bool _showEmailOnBill = false;
    [ObservableProperty] private bool _showGstOnBill = true;
    [ObservableProperty] private bool _showFoodLicenseOnBill = false;
    [ObservableProperty] private bool _showAddressOnBill = true;

    // The app's highlight colour, per business. Applied live and saved the instant a swatch is
    // picked — it isn't part of the branding form, so it doesn't wait on Save Branding.
    [ObservableProperty] private string _accentColor = ThemeService.Default;

    /// <summary>What the custom-colour box currently holds — bound so the preview swatch beside
    /// it tracks the typing, and seeded with the active colour when the screen loads.</summary>
    [ObservableProperty] private string _customColorInput = ThemeService.Default;

    /// <summary>The accent swatches shown on the profile page.</summary>
    public ObservableCollection<ThemeSwatch> ThemeColors { get; } = new();

    public string StoreInitials
    {
        get
        {
            var name = (StoreName ?? "").Trim();
            if (string.IsNullOrEmpty(name)) return "?";
            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) return (parts[0][0].ToString() + parts[1][0]).ToUpperInvariant();
            return name.Length >= 2 ? name.Substring(0, 2).ToUpperInvariant() : name.ToUpperInvariant();
        }
    }

    // Security PIN
    [ObservableProperty] private string _pinMessage = "";
    [ObservableProperty] private bool _pinMessageIsError;

    // Printer
    [ObservableProperty] private string _selectedPrinter = "";
    [ObservableProperty] private string _paperSize = "80mm";
    [ObservableProperty] private int _printCopies = 1;
    // Empty for the same reason as the profile above: a default UPI id is a real account,
    // and a bill QR pointing at the wrong business takes the customer's money with it.
    [ObservableProperty] private string _upiId = "";
    [ObservableProperty] private string _upiName = "";
    [ObservableProperty] private string _upiPhone = "";
    [ObservableProperty] private string _qrImagePath = "";
    [ObservableProperty] private bool _printQrCodeOnBill = true;
    [ObservableProperty] private bool _dailyResetBillCounter = false;

    [ObservableProperty] private string _savedMessage = "";

    public ObservableCollection<string> AvailablePrinters { get; } = new();
    public ObservableCollection<string> PaperSizes { get; } = new() { "58mm", "80mm" };

    [RelayCommand] private void RefreshPrinters() => LoadPrinters();

    // Shortcuts
    [ObservableProperty] private string _shortcutsSearch = "";
    public ObservableCollection<ShortcutItem> Shortcuts { get; } = new();
    public System.ComponentModel.ICollectionView ShortcutsView { get; }

    partial void OnShortcutsSearchChanged(string value) => ShortcutsView.Refresh();

    // Catalog (POS Defaults + Menu Items)
    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<TableEdit> Tables { get; } = new();
    public ObservableCollection<DiningArea> Areas { get; } = new();
    public ObservableCollection<GstRate> GstRates { get; } = new();
    public ObservableCollection<MenuItem> MenuItems { get; } = new();

    public int TableCount => Tables.Count;
    public int MenuCount => MenuItems.Count;
    public int CategoryCount => Categories.Count;

    // Menu Item Management: search + category/subcategory filters
    [ObservableProperty] private string _menuSearch = "";
    [ObservableProperty] private long _menuFilterCategoryId = -1;
    [ObservableProperty] private long _menuFilterSubcategoryId = -1;
    [ObservableProperty] private bool _isMenuSubcategoryFilterEnabled;

    public ObservableCollection<MenuItemRow> MenuItemRows { get; } = new();
    public ObservableCollection<FilterOption> CategoryFilterOptions { get; } = new();
    public ObservableCollection<FilterOption> SubcategoryFilterOptions { get; } = new();
    public System.ComponentModel.ICollectionView MenuItemRowsView { get; }

    partial void OnMenuSearchChanged(string value) => MenuItemRowsView.Refresh();

    partial void OnMenuFilterCategoryIdChanged(long value)
    {
        IsMenuSubcategoryFilterEnabled = value != -1;
        MenuFilterSubcategoryId = -1;
        BuildSubcategoryFilterOptions();
        // MenuFilterSubcategoryId may already have been -1 (a no-op set that raises no
        // change notification), so force the combo box to re-sync its display against
        // the just-rebuilt options list — otherwise it renders blank after Clear()+Add().
        OnPropertyChanged(nameof(MenuFilterSubcategoryId));
        MenuItemRowsView.Refresh();
    }

    partial void OnMenuFilterSubcategoryIdChanged(long value) => MenuItemRowsView.Refresh();

    public SettingsViewModel(AppSettingsRepository settings, CatalogRepository catalog, ClientContext client)
    {
        _settings = settings;
        _catalog = catalog;

        foreach (var p in ThemeService.Presets)
        {
            ThemeColors.Add(new ThemeSwatch(p.Name, p.Hex));
        }

        // The counter changes brand at a shift change, and this view model outlives it. Without
        // this the till would keep printing — and showing in the sidebar — the previous
        // business's name, GST number and UPI id after someone else signed in.
        client.Changed += () =>
        {
            // Wipe the branding first. Load() keeps the CURRENT value as its last fallback,
            // which is right when re-reading the same business's partial profile and wrong
            // across a shift change: a brand whose profile hasn't reached this counter yet
            // would otherwise inherit the previous brand's name, GST number and address —
            // and print them on its bills.
            ClearBranding();
            // Same leak as the branding: these are singleton fields that outlive the shift, so
            // a "Saved ✓ 07:13 PM" from the previous business would greet the next one on a
            // form it has not touched.
            SavedMessage = "";
            PinMessage = "";
            // From the server: the business has just changed, and this counter may never have
            // held the incoming one's profile at all.
            Load(fromServer: true);
            LoadShortcuts();
        };
        ShortcutsView = System.Windows.Data.CollectionViewSource.GetDefaultView(Shortcuts);
        ShortcutsView.Filter = o => o is not ShortcutItem s || string.IsNullOrWhiteSpace(ShortcutsSearch)
            || s.Action.Contains(ShortcutsSearch, StringComparison.OrdinalIgnoreCase)
            || s.Key.Contains(ShortcutsSearch, StringComparison.OrdinalIgnoreCase);
        MenuItemRowsView = System.Windows.Data.CollectionViewSource.GetDefaultView(MenuItemRows);
        MenuItemRowsView.Filter = o => o is MenuItemRow row && MatchesMenuFilter(row);
        LoadPrinters();
        Load();
        LoadShortcuts();
        ReloadCatalog();
    }

    private bool MatchesMenuFilter(MenuItemRow row)
    {
        var m = row.Item;
        if (!string.IsNullOrWhiteSpace(MenuSearch))
        {
            var nameMatch = m.Name.Contains(MenuSearch, StringComparison.OrdinalIgnoreCase);
            var codeMatch = m.Code?.Contains(MenuSearch, StringComparison.OrdinalIgnoreCase) == true;
            if (!nameMatch && !codeMatch) return false;
        }
        if (MenuFilterCategoryId != -1)
        {
            var cat = Categories.FirstOrDefault(c => c.Id == m.CategoryId);
            var isDirect = m.CategoryId == MenuFilterCategoryId;
            var isSub = cat != null && cat.ParentId == MenuFilterCategoryId;
            if (!isDirect && !isSub) return false;
        }
        if (MenuFilterSubcategoryId != -1 && m.SubCategoryId != MenuFilterSubcategoryId) return false;
        return true;
    }

    private void BuildMenuItemRows()
    {
        MenuItemRows.Clear();
        foreach (var m in MenuItems)
        {
            var cat = Categories.FirstOrDefault(c => c.Id == m.CategoryId);
            var sub = m.SubCategoryId.HasValue ? Categories.FirstOrDefault(c => c.Id == m.SubCategoryId) : null;
            MenuItemRows.Add(new MenuItemRow(m, cat?.Name ?? "Unassigned", sub?.Name ?? "-"));
        }
    }

    private int MenuCountFor(long catId, bool isSub)
    {
        if (isSub) return MenuItems.Count(m => m.SubCategoryId == catId);
        var childIds = Categories.Where(c => c.ParentId == catId).Select(c => c.Id).ToHashSet();
        return MenuItems.Count(m => m.CategoryId == catId || (m.SubCategoryId.HasValue && childIds.Contains(m.SubCategoryId.Value)));
    }

    private void BuildMenuFilterOptions()
    {
        CategoryFilterOptions.Clear();
        CategoryFilterOptions.Add(new FilterOption(-1, $"All Categories ({MenuItems.Count})"));
        foreach (var c in Categories.Where(IsTopLevel).OrderBy(c => c.SortOrder))
            CategoryFilterOptions.Add(new FilterOption(c.Id, $"{c.Name} ({MenuCountFor(c.Id, false)})"));
        BuildSubcategoryFilterOptions();
    }

    private void BuildSubcategoryFilterOptions()
    {
        SubcategoryFilterOptions.Clear();
        SubcategoryFilterOptions.Add(new FilterOption(-1, "All Subcategories"));
        if (MenuFilterCategoryId != -1)
        {
            foreach (var c in Categories.Where(c => c.ParentId == MenuFilterCategoryId).OrderBy(c => c.SortOrder))
                SubcategoryFilterOptions.Add(new FilterOption(c.Id, $"{c.Name} ({MenuCountFor(c.Id, true)})"));
        }
    }

    private void LoadPrinters()
    {
        AvailablePrinters.Clear();
        try
        {
            var srv = new System.Printing.LocalPrintServer();
            foreach (var q in srv.GetPrintQueues(new[]
            {
                System.Printing.EnumeratedPrintQueueTypes.Local,
                System.Printing.EnumeratedPrintQueueTypes.Connections
            }))
            {
                AvailablePrinters.Add(q.Name);
            }
        }
        catch { }
        // A machine with no printers installed gets an empty list, not an invented
        // "Default Thermal Printer" entry — that name matched no real queue, so picking it
        // only looked like a printer was configured while every print silently rerouted.
    }

    public void ReloadCatalog()
    {
        Categories.Clear();
        foreach (var c in _catalog.GetCategories()) Categories.Add(c);
        Tables.Clear();
        foreach (var t in _catalog.GetTables()) Tables.Add(t);
        Areas.Clear();
        foreach (var a in _catalog.GetAreas()) Areas.Add(a);
        GstRates.Clear();
        foreach (var g in _catalog.GetGstRates()) GstRates.Add(g);
        MenuItems.Clear();
        foreach (var m in _catalog.GetMenuItems()) MenuItems.Add(m);
        OnPropertyChanged(nameof(TableCount));
        OnPropertyChanged(nameof(MenuCount));
        OnPropertyChanged(nameof(CategoryCount));
        BuildCategoryRows();
        BuildMenuItemRows();
        BuildMenuFilterOptions();
        // Filter option lists are rebuilt from scratch above (Clear()+Add()); if the
        // currently selected filter value didn't change, force a re-sync so the combo
        // boxes don't render blank against the freshly rebuilt lists.
        OnPropertyChanged(nameof(MenuFilterCategoryId));
        OnPropertyChanged(nameof(MenuFilterSubcategoryId));
        MenuItemRowsView.Refresh();
        CatalogChanged?.Invoke();
    }

    /// <summary>
    /// Raised whenever menu items / categories / tables / areas change here, so the Orders
    /// screen can reload instead of showing stale catalog data until the app is restarted.
    /// </summary>
    public event Action? CatalogChanged;

    public ObservableCollection<CategoryRow> CategoryRows { get; } = new();

    private static bool IsTopLevel(Category c) => !c.ParentId.HasValue || c.ParentId.Value == 0;

    private void BuildCategoryRows()
    {
        CategoryRows.Clear();
        var parents = Categories.Where(IsTopLevel).OrderBy(c => c.SortOrder).ToList();
        foreach (var parent in parents)
        {
            var subs = Categories.Where(c => !IsTopLevel(c) && c.ParentId == parent.Id).OrderBy(c => c.SortOrder).ToList();
            var subIds = subs.Select(s => s.Id).ToHashSet();
            var parentCount = MenuItems.Count(m => m.CategoryId == parent.Id
                || (m.SubCategoryId.HasValue && subIds.Contains(m.SubCategoryId.Value)));
            CategoryRows.Add(new CategoryRow(parent, true, parentCount));

            foreach (var sub in subs)
            {
                var subCount = MenuItems.Count(m => m.SubCategoryId == sub.Id);
                CategoryRows.Add(new CategoryRow(sub, false, subCount));
            }
        }
    }

    [RelayCommand] private void MoveCategoryUp(CategoryRow row) => MoveCategory(row.Category, -1);
    [RelayCommand] private void MoveCategoryDown(CategoryRow row) => MoveCategory(row.Category, 1);

    private void MoveCategory(Category cat, int direction)
    {
        var siblings = Categories.Where(c => IsTopLevel(cat) ? IsTopLevel(c) : c.ParentId == cat.ParentId)
            .OrderBy(c => c.SortOrder).ToList();
        var idx = siblings.FindIndex(c => c.Id == cat.Id);
        var targetIdx = idx + direction;
        if (idx < 0 || targetIdx < 0 || targetIdx >= siblings.Count) return;

        var target = siblings[targetIdx];
        (cat.SortOrder, target.SortOrder) = (target.SortOrder, cat.SortOrder);
        _catalog.SaveCategory(cat);
        _catalog.SaveCategory(target);
        ReloadCatalog();
    }

    /// <summary>Deletes a category. If it's a subcategory, any menu items assigned to it are
    /// first re-parented onto its parent category (matching the Electron confirm-dialog behaviour).</summary>
    public void DeleteCategoryWithTransfer(Category c)
    {
        if (!IsTopLevel(c) && c.ParentId.HasValue)
        {
            var affected = MenuItems.Where(m => m.CategoryId == c.Id || m.SubCategoryId == c.Id).ToList();
            foreach (var item in affected)
            {
                item.CategoryId = c.ParentId;
                item.SubCategoryId = null;
                _catalog.SaveMenuItem(item);
            }
        }
        _catalog.DeleteCategory(c.Id);
        ReloadCatalog();
    }

    /// <summary>Drops the printed identity so nothing carries over between businesses.</summary>
    private void ClearBranding()
    {
        StoreName = "";
        StoreWebsite = "";
        StorePhone = "";
        StoreEmail = "";
        StoreGstNo = "";
        StoreFoodLicenseNo = "";
        StoreAddress = "";
        StoreLogoUrl = "";
    }

    /// <summary>
    /// Re-reads the settings from the database and refills the form.
    ///
    /// Called on the way in to the Settings screen, the way Reports reloads on the way in:
    /// saves go straight to the server now, so the form has to be filled from there too.
    /// Editing a stale local copy and pressing Save would push it back over a newer row.
    /// </summary>
    public void ReloadFromServer() => Load(fromServer: true);

    /// <param name="fromServer">
    /// False during construction. This runs on the UI thread while the app is still starting,
    /// and a server that is merely slow to refuse the connection would hold the window shut for
    /// as long as the timeout. The local mirror is what the till boots on; the screen re-reads
    /// from the database when it is actually opened, and after a sign-in changes the business.
    /// </param>
    private void Load(bool fromServer = false)
    {
        // Best-effort: offline this does nothing and the local mirror answers instead, which is
        // the whole reason the mirror is kept.
        if (fromServer)
        {
            _settings.RefreshFromServer();
        }

        // The highlight colour is this business's own, on its own key — read before the early
        // return below, so a client with no branding snapshot yet still shows its accent (or the
        // default green) selected. Re-applied here so opening Settings (which has just pulled the
        // latest from the server) repaints the app to whatever another till may have set.
        AccentColor = ThemeService.Normalize(
            _settings.GetJsonForClient<string>(ThemeService.SettingKey) ?? ThemeService.Default);
        ThemeService.Apply(AccentColor);
        SyncThemeSelection();

        var s = _settings.GetJsonForClient<SettingsSnapshot>(SettingsKey);
        var profile = _settings.GetJsonForClient<ProfileSnapshot>(ProfileKey);
        var upi = _settings.GetJsonForClient<UpiSnapshot>(UpiKey);
        // Printer settings used to live in the same blob. Reading the old one as a fallback
        // means an existing install keeps its printer instead of silently losing it.
        var device = _settings.GetJson<DeviceSnapshot>(DeviceSettingsKey);
        // No printer until somebody picks one. Defaulting to whatever the machine listed
        // first sent bills to "Microsoft Print to PDF", whose save dialog froze the till.
        // Canonical key wins; the old single blob is only a fallback for installs that saved
        // before the profile moved there.
        StoreName = Pick(profile?.Name, s?.StoreName, StoreName);
        StoreWebsite = Pick(profile?.Website, s?.StoreWebsite, StoreWebsite);
        StorePhone = Pick(profile?.ContactNumber, s?.StorePhone, StorePhone);
        StoreEmail = Pick(profile?.Email, s?.StoreEmail, StoreEmail);
        StoreGstNo = Pick(profile?.GstNumber, s?.StoreGstNo, StoreGstNo);
        StoreFoodLicenseNo = Pick(profile?.FoodLicenseNo, s?.StoreFoodLicenseNo, StoreFoodLicenseNo);
        StoreAddress = Pick(profile?.Address, s?.StoreAddress, StoreAddress);
        StoreLogoUrl = Pick(profile?.Logo, s?.StoreLogoUrl, StoreLogoUrl);

        if (s != null)
        {
            ShowNameOnBill = s.ShowNameOnBill;
            ShowWebsiteOnBill = s.ShowWebsiteOnBill;
            ShowPhoneOnBill = s.ShowPhoneOnBill;
            ShowEmailOnBill = s.ShowEmailOnBill;
            ShowAddressOnBill = s.ShowAddressOnBill;
            ShowGstOnBill = s.ShowGstOnBill;
            ShowFoodLicenseOnBill = s.ShowFoodLicenseOnBill;
            DailyResetBillCounter = _settings.GetJsonForClient<bool?>(DailyResetKey) ?? s.DailyResetBillCounter;
        }
        else
        {
            DailyResetBillCounter = _settings.GetJsonForClient<bool?>(DailyResetKey) ?? false;
        }

        UpiId = Pick(upi?.UpiId, s?.UpiId, UpiId);
        UpiName = Pick(upi?.UpiName, s?.UpiName, UpiName);
        UpiPhone = Pick(upi?.UpiPhone, s?.UpiPhone, UpiPhone);
        PrintQrCodeOnBill = upi?.PrintQrCode ?? s?.PrintQrCodeOnBill ?? true;

        var printer = device?.SelectedPrinter ?? s?.SelectedPrinter;
        PaperSize = device?.PaperSize ?? s?.PaperSize ?? PaperSize;
        PrintCopies = (device?.PrintCopies ?? s?.PrintCopies ?? 0) <= 0 ? 1 : (device?.PrintCopies ?? s?.PrintCopies ?? 1);
        QrImagePath = device?.QrImagePath ?? s?.QrImagePath ?? QrImagePath;
        SelectedPrinter = !string.IsNullOrEmpty(printer) && AvailablePrinters.Contains(printer) ? printer : "";
    }

    private const string ShortcutsKey = "pos_wpf_shortcuts";

    private static List<ShortcutItem> DefaultShortcuts() => new()
    {
        new ShortcutItem("item_search", "Item Search", "F1"),
        new ShortcutItem("save_kot", "Save KOT", "F2"),
        new ShortcutItem("print_kot", "Print KOT", "F3"),
        new ShortcutItem("print_bill", "Print Bill", "F4"),
        new ShortcutItem("settle_bill", "Settle Bill / Checkout", "F5"),
        new ShortcutItem("parcel", "Parcel Mode Toggle", "F7"),
        new ShortcutItem("transfer_table", "Transfer Table Order", "F11"),
        new ShortcutItem("extra_options", "Extra Options Panel", "Insert"),
        new ShortcutItem("close_popup", "Close Popup / Modal", "Escape"),
        new ShortcutItem("toggle_billing_mode", "Toggle Billing Mode (Quick / Table)", "Tab"),
        new ShortcutItem("change_table", "Change Table", "Alt+C"),
        new ShortcutItem("merge_table", "Merge Table", "Alt+M"),
        new ShortcutItem("split_table", "Split Table", "Alt+S"),
    };

    private void LoadShortcuts()
    {
        var saved = _settings.GetJsonForClient<List<ShortcutItem>>(ShortcutsKey);
        Shortcuts.Clear();
        foreach (var s in saved ?? DefaultShortcuts()) Shortcuts.Add(s);
    }

    private void PersistShortcuts() => _settings.SetJsonSynced(ShortcutsKey, Shortcuts.ToList());

    /// <summary>Returns the key string currently bound to <paramref name="actionId"/> (e.g. "F3", "Alt+C").</summary>
    public string KeyFor(string actionId)
        => Shortcuts.FirstOrDefault(s => s.Id == actionId)?.Key ?? "";

    /// <summary>
    /// True when the pressed key+modifiers match the shortcut configured for <paramref name="actionId"/>.
    /// Lets the main window honour user-remapped hotkeys instead of hard-coded ones.
    /// </summary>
    public bool ShortcutMatches(string actionId, System.Windows.Input.Key key, System.Windows.Input.ModifierKeys modifiers)
    {
        var binding = KeyFor(actionId);
        if (string.IsNullOrWhiteSpace(binding)) return false;

        var parts = binding.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        var expected = System.Windows.Input.ModifierKeys.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            expected |= parts[i].ToLowerInvariant() switch
            {
                "ctrl" or "control" => System.Windows.Input.ModifierKeys.Control,
                "alt" => System.Windows.Input.ModifierKeys.Alt,
                "shift" => System.Windows.Input.ModifierKeys.Shift,
                "win" => System.Windows.Input.ModifierKeys.Windows,
                _ => System.Windows.Input.ModifierKeys.None
            };
        }

        var keyToken = parts[^1];
        // "Esc"/"Del" style abbreviations aren't valid Key enum names.
        keyToken = keyToken.ToLowerInvariant() switch
        {
            "esc" => "Escape",
            "del" => "Delete",
            "ins" => "Insert",
            _ => keyToken
        };
        if (!Enum.TryParse<System.Windows.Input.Key>(keyToken, ignoreCase: true, out var expectedKey)) return false;

        return key == expectedKey && modifiers == expected;
    }

    public void UpdateShortcutKey(ShortcutItem item, string newKey)
    {
        item.Key = newKey;
        PersistShortcuts();
    }

    [RelayCommand]
    private void SaveShortcutsAsDefault()
    {
        PersistShortcuts();
        SavedMessage = $"Shortcuts saved as default ✓  {DateTime.Now:hh:mm tt}";
    }

    [RelayCommand]
    private void ResetShortcutsToDefault()
    {
        Shortcuts.Clear();
        foreach (var s in DefaultShortcuts()) Shortcuts.Add(s);
        PersistShortcuts();
        SavedMessage = $"Shortcuts reset to default ✓  {DateTime.Now:hh:mm tt}";
    }

    [RelayCommand] private void SelectTab(string tab) => ActiveTab = tab;
    [RelayCommand] private void SelectSubTab(string tab) => ActiveSubTab = tab;

    /// <summary>Swatch click. The picker's custom-colour button calls
    /// <see cref="SetThemeColor"/> directly with the hex it produced.</summary>
    [RelayCommand]
    private void SelectThemeColor(string hex) => SetThemeColor(hex);

    /// <summary>
    /// Sets the app's highlight colour for the signed-in business.
    ///
    /// Takes effect at once — the whole app repaints on the click — and is saved on the same
    /// click. Saved per business and pushed to the server like the rest of the profile, so the
    /// brand keeps its colour on any till it signs in to, not just this one. Nothing else on the
    /// form is touched, so it doesn't wait on Save Branding.
    /// </summary>
    public void SetThemeColor(string hex)
    {
        AccentColor = ThemeService.Normalize(hex);
        ThemeService.Apply(AccentColor);
        _settings.SetJsonSynced(ThemeService.SettingKey, AccentColor);
        SyncThemeSelection();
    }

    /// <summary>
    /// Applies a colour typed into the custom box. Any WPF-parseable colour is allowed — a hex
    /// like <c>#3FA9F5</c>, or a name like <c>DodgerBlue</c>. A value that doesn't parse is left
    /// alone, so a half-typed entry can't blank the app out.
    /// </summary>
    [RelayCommand]
    private void ApplyCustomColor()
    {
        if (ThemeService.TryParse(CustomColorInput, out var hex))
        {
            SetThemeColor(hex);
        }
    }

    /// <summary>Lights the ring on whichever swatch matches the current colour, and keeps the
    /// custom box showing it so a re-open starts from the colour in use.</summary>
    private void SyncThemeSelection()
    {
        foreach (var swatch in ThemeColors)
        {
            swatch.IsSelected = string.Equals(swatch.Hex, AccentColor, StringComparison.OrdinalIgnoreCase);
        }
        CustomColorInput = AccentColor;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        // The shop's identity goes into restaurant_profile — the key the dashboard, the mobile
        // menu and the Electron app all read. Keeping it in a WPF-only key meant renaming the
        // shop here changed nothing anywhere else, which is exactly what it looked like.
        // The field names are theirs; address and foodLicenseNo are extra keys they ignore.
        var onServer = _settings.SetJsonSynced(ProfileKey, new ProfileSnapshot
        {
            Name = StoreName, Website = StoreWebsite, Email = StoreEmail,
            GstNumber = StoreGstNo, ContactNumber = StorePhone, Logo = StoreLogoUrl,
            Address = StoreAddress, FoodLicenseNo = StoreFoodLicenseNo
        });

        // The clients row carries the business name for everything that isn't the bill — and
        // the server does the same on its side when this profile lands there.
        _settings.SetClientName(StoreName);

        _settings.SetJsonSynced(UpiKey, new UpiSnapshot
        {
            UpiId = UpiId, UpiName = UpiName, UpiPhone = UpiPhone, PrintQrCode = PrintQrCodeOnBill
        });

        _settings.SetJsonSynced(DailyResetKey, DailyResetBillCounter);

        // What's left is genuinely WPF's own: which of those details get printed on the bill.
        _settings.SetJsonSynced(SettingsKey, new SettingsSnapshot
        {
            ShowNameOnBill = ShowNameOnBill, ShowWebsiteOnBill = ShowWebsiteOnBill,
            ShowPhoneOnBill = ShowPhoneOnBill, ShowEmailOnBill = ShowEmailOnBill, ShowAddressOnBill = ShowAddressOnBill,
            ShowGstOnBill = ShowGstOnBill, ShowFoodLicenseOnBill = ShowFoodLicenseOnBill
        });

        // Device-local: which printer this counter is plugged into, its paper width, and the
        // path to a QR image on this disk. Deliberately NOT synced — pushing these would give
        // the second counter the first counter's printer name and a file path that isn't there.
        _settings.SetJson(DeviceSettingsKey, new DeviceSnapshot
        {
            SelectedPrinter = SelectedPrinter, PaperSize = PaperSize,
            PrintCopies = PrintCopies, QrImagePath = QrImagePath
        });

        // The two outcomes are genuinely different and the operator has to be able to tell them
        // apart. "Saved ✓" on a save that only reached SQLite is what sent someone looking in
        // MySQL for a change that was never sent.
        SavedMessage = onServer
            ? $"Saved ✓  {DateTime.Now:hh:mm tt}"
            : $"Locally saved — server offline, baad me sync hoga  {DateTime.Now:hh:mm tt}";
    }

    private const string PinKey = "login_pin";

    public bool UpdatePin(string newPin, string confirmPin)
    {
        if (string.IsNullOrWhiteSpace(newPin) || string.IsNullOrWhiteSpace(confirmPin))
        {
            PinMessage = "Kripya sabhi fields bharein!"; PinMessageIsError = true; return false;
        }
        if (newPin.Length != 4 || !newPin.All(char.IsDigit))
        {
            PinMessage = "PIN 4 digits ka hona chahiye!"; PinMessageIsError = true; return false;
        }
        if (newPin != confirmPin)
        {
            PinMessage = "Naya PIN aur Confirm PIN match nahi ho raha hai!"; PinMessageIsError = true; return false;
        }
        _settings.SetSynced(PinKey, newPin);
        PinMessage = $"Security PIN update ho gaya ✓  {DateTime.Now:hh:mm tt}";
        PinMessageIsError = false;
        return true;
    }

    // ── Catalog CRUD (called by the view code-behind after modal input) ──
    public void SaveArea(DiningArea a) { _catalog.SaveArea(a); ReloadCatalog(); }
    public void SaveTable(TableEdit t) { _catalog.SaveTable(t); ReloadCatalog(); }
    public void SaveCategory(Category c) { _catalog.SaveCategory(c); ReloadCatalog(); }
    public void SaveGstRate(GstRate g) { _catalog.SaveGstRate(g); ReloadCatalog(); }
    public void SaveMenuItem(MenuItem m) { _catalog.SaveMenuItem(m); ReloadCatalog(); }

    [RelayCommand] private void DeleteArea(DiningArea a) { _catalog.DeleteArea(a.Id); ReloadCatalog(); }
    [RelayCommand] private void DeleteTable(TableEdit t) { _catalog.DeleteTable(t.Id); ReloadCatalog(); }
    [RelayCommand] private void DeleteGstRate(GstRate g) { _catalog.DeleteGstRate(g.Id); ReloadCatalog(); }
    [RelayCommand] private void DeleteMenuItem(MenuItem m) { _catalog.DeleteMenuItem(m.Id); ReloadCatalog(); }

    public string CategoryNameFor(long? categoryId)
        => Categories.FirstOrDefault(c => c.Id == categoryId)?.Name ?? "—";

    /// <summary>Snapshot of the printer + branding settings for a receipt print.</summary>
    public Pos.Core.Printing.PrintConfig BuildPrintConfig() => new()
    {
        PrinterName = SelectedPrinter,
        PaperSize = PaperSize,
        Copies = PrintCopies,
        StoreName = StoreName,
        Website = StoreWebsite,
        Phone = StorePhone,
        Email = StoreEmail,
        GstNo = StoreGstNo,
        FoodLicenseNo = StoreFoodLicenseNo,
        Address = StoreAddress,
        ShowName = ShowNameOnBill,
        ShowWebsite = ShowWebsiteOnBill,
        ShowPhone = ShowPhoneOnBill,
        ShowEmail = ShowEmailOnBill,
        ShowGst = ShowGstOnBill,
        ShowFoodLicense = ShowFoodLicenseOnBill,
        ShowAddress = ShowAddressOnBill,
        QrImagePath = QrImagePath,
        PrintQrOnBill = PrintQrCodeOnBill,
        // With a UPI id set, the bill's QR is built per bill with the amount filled in; the
        // uploaded image is only the fallback for a shop that has no id.
        UpiId = UpiId,
        UpiName = UpiName
    };

    /// <summary>
    /// What this app keeps for itself: which of the profile details are printed on the bill.
    ///
    /// The Store*/Upi*/DailyReset properties are still here, but only so an install that saved
    /// before the profile moved to restaurant_profile can still be read. Nothing writes them
    /// any more.
    /// </summary>
    public sealed class SettingsSnapshot
    {
        public string? StoreName { get; set; }
        public string? StoreWebsite { get; set; }
        public string? StorePhone { get; set; }
        public string? StoreEmail { get; set; }
        public string? StoreGstNo { get; set; }
        public string? StoreFoodLicenseNo { get; set; }
        public string? StoreAddress { get; set; }
        public string? StoreLogoUrl { get; set; }
        public bool ShowNameOnBill { get; set; }
        public bool ShowWebsiteOnBill { get; set; }
        public bool ShowPhoneOnBill { get; set; }
        public bool ShowEmailOnBill { get; set; }
        public bool ShowAddressOnBill { get; set; }
        public bool ShowGstOnBill { get; set; }
        public bool ShowFoodLicenseOnBill { get; set; }
        public string? SelectedPrinter { get; set; }
        public string? PaperSize { get; set; }
        public int PrintCopies { get; set; }
        public string? UpiId { get; set; }
        public string? UpiName { get; set; }
        public string? UpiPhone { get; set; }
        public string? QrImagePath { get; set; }
        public bool PrintQrCodeOnBill { get; set; }
        public bool DailyResetBillCounter { get; set; }
    }

    /// <summary>restaurant_profile, in the field names the rest of the system expects.</summary>
    public sealed class ProfileSnapshot
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("website")] public string? Website { get; set; }
        [JsonPropertyName("email")] public string? Email { get; set; }
        [JsonPropertyName("gstNumber")] public string? GstNumber { get; set; }
        [JsonPropertyName("contactNumber")] public string? ContactNumber { get; set; }
        [JsonPropertyName("logo")] public string? Logo { get; set; }
        [JsonPropertyName("address")] public string? Address { get; set; }
        [JsonPropertyName("foodLicenseNo")] public string? FoodLicenseNo { get; set; }
    }

    /// <summary>upi_settings, likewise.</summary>
    public sealed class UpiSnapshot
    {
        [JsonPropertyName("upiId")] public string? UpiId { get; set; }
        [JsonPropertyName("upiName")] public string? UpiName { get; set; }
        [JsonPropertyName("upiPhone")] public string? UpiPhone { get; set; }
        [JsonPropertyName("printQrCode")] public bool PrintQrCode { get; set; }
    }

    /// <summary>The half of the settings that is about this counter's hardware.</summary>
    public sealed class DeviceSnapshot
    {
        public string? SelectedPrinter { get; set; }
        public string? PaperSize { get; set; }
        public int PrintCopies { get; set; }
        public string? QrImagePath { get; set; }
    }
}

public sealed partial class ShortcutItem : ObservableObject
{
    public string Id { get; set; }
    public string Action { get; set; }
    [ObservableProperty] private string _key;

    public ShortcutItem() { Id = ""; Action = ""; _key = ""; }

    public ShortcutItem(string id, string action, string key)
    {
        Id = id; Action = action; _key = key;
    }
}

/// <summary>Flattened parent/subcategory row for the "Categories &amp; Subcategories" table.</summary>
public sealed class CategoryRow
{
    public Category Category { get; }
    public bool IsParent { get; }
    public int ItemCount { get; }

    public string Name => Category.Name;
    public long SortOrder => Category.SortOrder;

    public CategoryRow(Category category, bool isParent, int itemCount)
    {
        Category = category;
        IsParent = isParent;
        ItemCount = itemCount;
    }
}

/// <summary>Option for the Menu Items category/subcategory filter dropdowns (Id = -1 means "All").</summary>
public sealed class FilterOption
{
    public long Id { get; }
    public string Label { get; }
    public FilterOption(long id, string label) { Id = id; Label = label; }
    public override string ToString() => Label;
}

/// <summary>Menu item row flattened with its category/subcategory display names for the Menu Item Management table.</summary>
public sealed class MenuItemRow
{
    public MenuItem Item { get; }
    public string CategoryName { get; }
    public string SubcategoryName { get; }
    public string Name => Item.Name;
    public string? Code => Item.Code;
    public double Price => Item.Price;

    public MenuItemRow(MenuItem item, string categoryName, string subcategoryName)
    {
        Item = item;
        CategoryName = categoryName;
        SubcategoryName = subcategoryName;
    }
}

/// <summary>One accent choice on the profile page: the colour to paint the swatch, the hex the
/// pick command carries, and whether it is the one currently in use (which lights its ring).</summary>
public sealed partial class ThemeSwatch : ObservableObject
{
    public string Name { get; }
    public string Hex { get; }
    public Brush Swatch { get; }

    [ObservableProperty] private bool _isSelected;

    public ThemeSwatch(string name, string hex)
    {
        Name = name;
        Hex = ThemeService.Normalize(hex);
        Swatch = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Hex));
    }
}
