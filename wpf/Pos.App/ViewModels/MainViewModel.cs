using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.App.Helpers;
using Pos.App.Services;
using Pos.Core;
using Pos.Core.Data;
using Pos.Core.Models;
using Pos.Core.Repositories;
using Pos.Core.Sync;

namespace Pos.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly MenuRepository _menu;
    private readonly TableRepository _tables;
    private readonly OrderRepository _orders;
    private readonly QuickNotesRepository _quickNotes;
    private readonly SyncCoordinator _sync;

    /// <summary>The business this shift belongs to — used as the sidebar's fallback name when
    /// its profile has not reached this counter yet.</summary>
    private readonly ClientContext _client;
    private readonly System.Windows.Threading.Dispatcher _dispatcher;

    /// <summary>Receipts go out through here, never on the UI thread — see PrintSpooler.</summary>
    private readonly Services.PrintSpooler _printer = new();

    private List<MenuItem> _allItems = new();
    private List<TableView> _allTables = new();

    /// <summary>Item ID → total sold quantity across all settled bills.
    /// Loaded once with the catalog and refreshed after each sale so search
    /// suggestions rank best-sellers first.</summary>
    private Dictionary<long, long> _popularityScores = new();

    public ObservableCollection<AreaTab> Areas { get; } = new();
    public ObservableCollection<TableView> Tables { get; } = new();     // filtered by area
    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<CategoryTabVM> CategoryTabs { get; } = new();
    public FastObservableCollection<MenuItem> VisibleItems { get; } = new();

    /// <summary>Best-sellers for the "Most Selling Items" panel — items sold more than ten times.
    /// Refreshed after every bill so it reflects the day as it builds.</summary>
    public ObservableCollection<PopularItem> PopularItems { get; } = new();
    public bool HasPopularItems => PopularItems.Count > 0;

    /// <summary>
    /// What the search dropdown shows: the top <see cref="MaxSuggestions"/> matches.
    /// The dropdown's open cost grows with the number of rows it has to lay out, so an
    /// unbounded list made every keystroke that changed the match set feel laggy. The
    /// match ordering already puts exact code/name hits first, so a short list loses
    /// nothing for the scan-code flow while keeping typing responsive.
    /// </summary>
    public FastObservableCollection<MenuItem> Suggestions { get; } = new();

    private const int MaxSuggestions = 15;
    public ObservableCollection<CartLine> Cart { get; } = new();

    [ObservableProperty] private TableView? _selectedTable;
    [ObservableProperty] private AreaTab? _selectedArea;
    [ObservableProperty] private Category? _selectedCategory;
    [ObservableProperty] private CategoryTabVM? _selectedCategoryTab;
    [ObservableProperty] private MenuItem? _selectedMenuItem;
    [ObservableProperty] private bool _isSearchPopupOpen;
    [ObservableProperty] private string _centerMode = "Table";   // "Table" | "Menu"
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewTableOrder))]
    private string _billMode = "Quick";     // "Quick" | "Table" | "QR"
    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                var wasEmpty = string.IsNullOrEmpty(_searchText);
                _searchText = value ?? "";
                RefreshVisibleItems();
                IsSearchPopupOpen = !string.IsNullOrWhiteSpace(_searchText) && Suggestions.Count > 0;
                if (wasEmpty || string.IsNullOrEmpty(_searchText))
                {
                    OnPropertyChanged(nameof(SearchText));
                }
            }
        }
    }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewTableOrder))]
    private string _selectedCartTab = "New";

    [ObservableProperty] private bool _isAddingExtra;
    [ObservableProperty] private bool _isParcelMode;

    partial void OnIsAddingExtraChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCartPanelVisible));
    }

    public bool IsCartPanelVisible => Cart.Count > 0 || IsAddingExtra;

    public IEnumerable<CartLine> OldOrderItems => Cart.Where(l => l.IsSaved);
    public IEnumerable<CartLine> NewOrderItems => Cart.Where(l => !l.IsSaved);

    public IEnumerable<CartLine> DisplayCartItems
    {
        get
        {
            if (!HasExistingOrder) return Cart;
            return SelectedCartTab == "Old" ? OldOrderItems : NewOrderItems;
        }
    }

    private System.ComponentModel.ICollectionView? _cartView;

    /// <summary>
    /// The grouped/filtered cart view bound by the order panel.
    ///
    /// Built once and cached. Re-creating it (or clearing and re-adding GroupDescriptions)
    /// on every notification forces a full regroup + re-render, which dropped the selection,
    /// reset the scroll position and made the panel feel broken on every qty change.
    /// Live shaping lets WPF react to IsSaved/IsParcel changes without any manual Refresh.
    /// </summary>
    public System.ComponentModel.ICollectionView DisplayCartItemsView
    {
        get
        {
            if (_cartView != null) return _cartView;

            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(Cart);
            view.Filter = item =>
            {
                if (item is not CartLine line) return false;
                if (!HasExistingOrder) return true;
                return SelectedCartTab == "Old" ? line.IsSaved : !line.IsSaved;
            };
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(CartLine.IsParcel)));

            if (view is System.ComponentModel.ICollectionViewLiveShaping live)
            {
                live.LiveFilteringProperties.Add(nameof(CartLine.IsSaved));
                live.IsLiveFiltering = true;
                live.LiveGroupingProperties.Add(nameof(CartLine.IsParcel));
                live.IsLiveGrouping = true;
            }

            _cartView = view;
            return _cartView;
        }
    }

    /// <summary>Re-applies the cart filter. Only needed when the filter depends on something
    /// that isn't a live-shaped item property (i.e. the Old/New tab or HasExistingOrder).</summary>
    private void RefreshCartView() => _cartView?.Refresh();

    /// <summary>
    /// After a table order is saved, everything now in the cart is part of that saved order.
    /// Mark every line saved, collapse duplicates, and move to the Old-order tab so the operator
    /// sees the whole order together on the first press.
    ///
    /// The KOT/bill methods run their preceding <see cref="LoadTables"/> under
    /// <see cref="_suppressCartReload"/>, so the cart is NOT torn down and rebuilt before we get
    /// here. That matters: the old code let the reload clear and re-add the cart (bouncing
    /// HasExistingOrder false→true, which collapses and re-shows the tab strip), and WPF's tab
    /// header binding cached "New" across that churn — so the switch only "took" on the second KOT.
    /// With the reload suppressed, HasExistingOrder stays true and this is a clean New→Old change
    /// the header re-reads immediately.
    /// </summary>
    private void ShowMergedOldOrder()
    {
        foreach (var line in Cart)
        {
            line.IsSaved = true;
        }
        MergeSavedLines();
        HasExistingOrder = true;
        SelectedCartTab = "Old";
        RaiseTotals();
        OnPropertyChanged(nameof(IsNewTableOrder));
        OnPropertyChanged(nameof(DisplayCartItems));
        RefreshCartView();
    }

    /// <summary>
    /// The shop's name in the sidebar, from the saved profile — not a constant. Renaming the
    /// shop in Settings used to change the bill but leave the sidebar reading the old name.
    /// </summary>
    /// <remarks>
    /// Falls back to the signed-in business rather than a constant. A brand whose profile has
    /// not synced to this counter yet has no StoreName of its own, and without this the sidebar
    /// and the draft label would keep showing the PREVIOUS brand's name — a Chay Chaupal shift
    /// reading "Daal Roti" all day.
    /// </remarks>
    public string ClientName =>
        Settings.StoreName is { Length: > 0 } name ? name
        : _client.Name is { Length: > 0 } fromClient ? fromClient
        : "POS";

    /// <summary>The two letters on the round logo, taken from the same name.</summary>
    public string ClientInitials =>
        Settings.StoreName is { Length: > 0 } ? Settings.StoreInitials : Initials(ClientName);

    private static string Initials(string name)
    {
        var words = (name ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 2
            ? string.Concat(words[0][0], words[1][0]).ToUpperInvariant()
            : (name ?? "").PadRight(2).Substring(0, 2).Trim().ToUpperInvariant();
    }

    /// <summary>The shop's phone under the sidebar footer — same source as the bill's.</summary>
    public string StorePhone => Settings.StorePhone;
    // ── Bill totals ─────────────────────────────────────────────────────────
    // Subtotal is what the lines add up to; the discount comes off that, and the grand
    // total is what the customer actually pays and what lands in orders.total_amount.
    [ObservableProperty] private string _discountType = "percentage";   // "percentage" | "flat"
    [ObservableProperty] private double _discountValue;

    public double Subtotal => Cart.Sum(l => l.LineTotal);

    public double DiscountAmount
    {
        get
        {
            if (DiscountValue <= 0) return 0;
            var raw = DiscountType == "percentage" ? Subtotal * DiscountValue / 100 : DiscountValue;
            return Math.Round(Math.Clamp(raw, 0, Subtotal), 2);
        }
    }

    public bool HasDiscount => DiscountAmount > 0;

    public string DiscountLabel => DiscountType == "percentage"
        ? $"{DiscountValue:0.##}%" : $"Flat ₹{DiscountValue:0.##}";

    public double GrandTotal => Math.Max(0, Subtotal - DiscountAmount);

    public void SetDiscount(string type, double value)
    {
        DiscountType = type;
        DiscountValue = Math.Max(0, value);
        RaiseTotals();
    }

    public void ClearDiscount() => SetDiscount("percentage", 0);

    public long TotalQty => Cart.Sum(l => Math.Max(1, l.Qty));
    public bool HasItems => Cart.Count > 0;
    public string BillTitle => BillMode == "Table" && SelectedTable != null
        ? $"TABLE {SelectedTable.TableNumber}" : "QUICK BILL";
    public string DraftLabel => $"#{ClientInitials}-DRAFT";

    [ObservableProperty] private string _activeScreen = "Orders";

    public LedgerViewModel Ledger { get; }
    public SettingsViewModel Settings { get; }
    public ReportsViewModel Reports { get; }
    public NotesViewModel Notes { get; }
    public QrOrderViewModel Qr { get; }

    public MainViewModel(MenuRepository menu, TableRepository tables, OrderRepository orders, QuickNotesRepository quickNotes, SyncCoordinator sync, LedgerViewModel ledger, SettingsViewModel settings, ReportsViewModel reports, NotesViewModel notes, QrOrderViewModel qr, ClientContext client)
    {
        _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        _menu = menu;
        _tables = tables;
        _orders = orders;
        _quickNotes = quickNotes;
        _sync = sync;
        _client = client;
        Ledger = ledger;
        Settings = settings;
        Reports = reports;
        Notes = notes;
        Qr = qr;

        // A QR order the operator accepts is written straight to its table; reload so the floor
        // shows it as occupied with the right amount without waiting for the next refresh.
        Qr.OrderAccepted += () =>
        {
            LoadTables();
            if (SelectedTable != null)
            {
                // Re-open the current table so its cart picks up items the customer just added.
                var id = SelectedTable.Id;
                SelectedTable = null;
                SelectedTable = Tables.FirstOrDefault(t => t.Id == id);
            }
        };

        Cart.CollectionChanged += (_, _) => RaiseTotals();
        // Settings edits (menu items, categories, tables, areas) must reflect on the Orders
        // screen immediately — otherwise it keeps serving stale catalog data until restart.
        Settings.CatalogChanged += () =>
        {
            LoadCatalog();
            LoadTables();
        };
        // The sidebar's name and logo read straight off the settings, so they have to be told
        // when those change — otherwise renaming the shop only shows up after a restart.
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SettingsViewModel.StoreName) or nameof(SettingsViewModel.StoreInitials))
            {
                OnPropertyChanged(nameof(ClientName));
                OnPropertyChanged(nameof(ClientInitials));
                OnPropertyChanged(nameof(DraftLabel));
            }
            else if (e.PropertyName == nameof(SettingsViewModel.StorePhone))
            {
                OnPropertyChanged(nameof(StorePhone));
            }
        };
        LoadCatalog();
        LoadTables();
        RefreshSavedNotesState();
        LoadPopularItems();

        _printer.Failed += (what, error) =>
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(new Action(() => ShowPrintError(what, error)));
            }
        };

        // Opens the print queue and warms WPF's XPS path now, while nobody is at the counter.
        _printer.Warmup(Settings.BuildPrintConfig());

        _sync.StatusChanged += OnSyncStatusChanged;
        ShowSync(_sync.Status);

        // Check for a newer build in the background — never blocks startup, and stays quiet unless
        // there's actually something to offer.
        _ = CheckForUpdatesAsync();
    }

    // ── App version / auto-update (footer) ──────────────────────────────────
    [ObservableProperty] private string _appVersion = AppInfo.DisplayVersion;
    [ObservableProperty] private string _updateText = "APP UP TO DATE";
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private bool _updateFailed;
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private double _updateProgress;
    [ObservableProperty] private string? _updateTooltip;

    private Velopack.UpdateManager? _updateManager;
    private Velopack.UpdateInfo? _pendingUpdate;

    /// <summary>
    /// Asks the release feed whether a newer build is out and, if so, lights up the footer's update
    /// badge. Silent otherwise, and a no-op when the app isn't a Velopack install (a dev run or a
    /// plain copied folder) — there's nothing for the updater to replace in that case.
    /// </summary>
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var mgr = new Velopack.UpdateManager(AppInfo.UpdateFeedUrl);
            if (!mgr.IsInstalled)
            {
                return;
            }
            _updateManager = mgr;

            var info = await mgr.CheckForUpdatesAsync();
            OnUi(() => ApplyUpdateInfo(info));
        }
        catch
        {
            // Offline or an unreachable feed just leaves the footer saying "up to date".
        }
    }

    private void ApplyUpdateInfo(Velopack.UpdateInfo? info)
    {
        _pendingUpdate = info;
        UpdateFailed = false;
        if (info != null)
        {
            var v = info.TargetFullRelease.Version.ToString();
            UpdateAvailable = true;
            UpdateText = $"UPDATE — v{v}";
            UpdateTooltip = $"Naya version v{v} available hai. Click karke update karein.";
        }
        else
        {
            UpdateAvailable = false;
            UpdateText = "APP UP TO DATE";
            UpdateTooltip = $"Aap latest build par hain ({AppInfo.DisplayVersion}).";
        }
    }

    /// <summary>
    /// Downloads and installs the waiting update, then restarts — Velopack does the file swap from
    /// outside the process, so this never has to touch a running exe. Guarded by a confirm so it
    /// can't interrupt billing without the operator's say-so.
    /// </summary>
    [RelayCommand]
    private async Task UpdateNow()
    {
        if (_updateManager is null || _pendingUpdate is null || IsUpdating)
        {
            return;
        }

        var v = _pendingUpdate.TargetFullRelease.Version.ToString();
        var confirm = System.Windows.MessageBox.Show(
            $"Version v{v} install karein? App band ho ke naye version me khul jayegi.",
            "App Update",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        IsUpdating = true;
        UpdateFailed = false;
        UpdateText = "DOWNLOADING… 0%";
        try
        {
            await _updateManager.DownloadUpdatesAsync(_pendingUpdate, percent =>
            {
                var app = System.Windows.Application.Current;
                if (app != null)
                {
                    app.Dispatcher.Invoke(() =>
                    {
                        UpdateProgress = percent / 100.0;
                        UpdateText = $"DOWNLOADING… {percent}%";
                    }, System.Windows.Threading.DispatcherPriority.Render);
                }
                else
                {
                    OnUi(() =>
                    {
                        UpdateProgress = percent / 100.0;
                        UpdateText = $"DOWNLOADING… {percent}%";
                    });
                }
            });

            UpdateText = "INSTALLING…";
            // Swaps in the new version and relaunches; the process ends here.
            _updateManager.ApplyUpdatesAndRestart(_pendingUpdate);
        }
        catch (Exception ex)
        {
            IsUpdating = false;
            UpdateFailed = true;
            UpdateText = "UPDATE FAILED";
            UpdateTooltip = "Update fail ho gaya: " + ex.Message + ". Click to retry.";
            System.Windows.MessageBox.Show("Update fail ho gaya: " + ex.Message, "App Update",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>Runs an action on the UI thread, whether the caller is already on it or on one of
    /// Velopack's background callbacks.</summary>
    private void OnUi(Action action)
    {
        if (_dispatcher != null && !_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    // ── Sync status (footer) ────────────────────────────────────────────────
    [ObservableProperty] private string _syncText = "SYNC…";
    [ObservableProperty] private string _syncState = "idle";   // idle | ok | pending | offline
    [ObservableProperty] private string? _syncTooltip;

    /// <summary>The coordinator reports from a background thread; the UI can only be touched
    /// on its own.</summary>
    private void OnSyncStatusChanged(SyncStatus status)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => ShowSync(status)));
            return;
        }
        ShowSync(status);
    }

    private void ShowSync(SyncStatus status)
    {
        var when = status.LastSuccessIst is { } t ? $" {t:HH:mm}" : "";
        if (!status.Online)
        {
            SyncState = "offline";
            SyncText = status.Pending > 0 ? $"OFFLINE · {status.Pending} PENDING" : "OFFLINE";
        }
        else if (status.Pending > 0)
        {
            SyncState = "pending";
            SyncText = $"{status.Pending} PENDING";
        }
        else
        {
            SyncState = "ok";
            SyncText = $"SYNCED{when}";
        }

        var nl = Environment.NewLine;
        SyncTooltip = string.IsNullOrWhiteSpace(status.LastError)
            ? $"Server: {_sync.ApiUrl}{nl}Baaki: {status.Pending}{nl}Click karke abhi sync karein"
            : $"Server: {_sync.ApiUrl}{nl}{status.LastError}{nl}Click karke dobara koshish karein";
    }

    [RelayCommand]
    private async Task SyncNow()
    {
        SyncState = "pending";
        SyncText = "SYNCING…";
        await _sync.SyncNowAsync();
    }

    [RelayCommand] private void NavTo(string screen) => ActiveScreen = screen;

    /// <summary>
    /// Reports reads from the database once and then sits on that result, so opening it after
    /// billing showed the figures from whenever it was last looked at — a bill just taken was
    /// simply missing. Refresh on the way in.
    /// </summary>
    partial void OnActiveScreenChanged(string value)
    {
        if (value == "Reports")
        {
            Reports.Reload();
        }
        // Same reason, one screen over: the shop's profile can be changed from the dashboard or
        // from another counter, and opening Settings on a stale copy means editing it.
        else if (value == "Settings")
        {
            Settings.ReloadFromServer();
        }
        // A bill put on a khata from the Reports screen writes straight to the repository; reload
        // Len-Den on the way in so the new udhaar is there without an app restart.
        else if (value == "Ledger")
        {
            Ledger.LoadData();
        }
    }

    private void LoadCatalog()
    {
        _allItems = _menu.GetMenuItems().ToList();
        _popularityScores = _orders.GetItemPopularityScores();
        Categories.Clear();
        CategoryTabs.Clear();

        CategoryTabs.Add(new CategoryTabVM
        {
            Category = null,
            Name = "ALL ITEMS",
            Count = _allItems.Count
        });

        foreach (var c in _menu.GetCategories())
        {
            Categories.Add(c);
            // Count items in this category AND in it as a sub-category. Sub-categories (Cold
            // Coffee, Burger…) hold their items through sub_category_id, not category_id, so a
            // count that only checked category_id showed them as empty even when they weren't.
            var cnt = _allItems.Count(i => i.CategoryId == c.Id || i.SubCategoryId == c.Id);
            CategoryTabs.Add(new CategoryTabVM
            {
                Category = c,
                Name = c.Name,
                Count = cnt
            });
        }
        SelectedCategoryTab = CategoryTabs.FirstOrDefault();
    }

    /// <summary>Refreshes the "Most Selling Items" panel. Cheap enough to run after each sale —
    /// one grouped query — so the panel keeps pace with the day.</summary>
    private void LoadPopularItems()
    {
        // Track the category the item grid is filtered to; the "All Items" tab (null category)
        // shows every best-seller.
        var categoryId = SelectedCategoryTab?.Category?.Id;
        PopularItems.Clear();
        foreach (var p in _orders.GetPopularItems(categoryId: categoryId))
        {
            PopularItems.Add(p);
        }
        OnPropertyChanged(nameof(HasPopularItems));

        // Also refresh the search-ranking scores so the next search reflects the latest sales.
        _popularityScores = _orders.GetItemPopularityScores();
    }

    /// <summary>True while <see cref="LoadTables"/> rebuilds the table list. The list box drops
    /// and re-picks its selection as the collection is cleared and refilled, and that churn must
    /// not be mistaken for the operator moving between tables (which would park/restore drafts).</summary>
    private bool _reloadingTables;

    private void LoadTables()
    {
        _reloadingTables = true;
        try
        {
            var currentId = SelectedTable?.Id;
            var currentArea = SelectedArea?.AreaValue;
            _allTables = _tables.All().ToList();

            Areas.Clear();
            Areas.Add(new AreaTab { Name = "ALL", AreaValue = null, Count = _allTables.Count });
            foreach (var g in _allTables
                         .GroupBy(t => string.IsNullOrWhiteSpace(t.AreaName) ? "OTHER" : t.AreaName!)
                         .OrderBy(g => g.Key))
            {
                Areas.Add(new AreaTab { Name = g.Key.ToUpperInvariant(), AreaValue = g.Key, Count = g.Count() });
            }
            SelectedArea = Areas.FirstOrDefault(a => a.AreaValue == currentArea) ?? Areas.FirstOrDefault();

            if (currentId != null)
            {
                SelectedTable = _allTables.FirstOrDefault(t => t.Id == currentId);
            }
        }
        finally
        {
            _reloadingTables = false;
        }
    }

    private void RefreshTables()
    {
        Tables.Clear();
        var area = SelectedArea?.AreaValue;
        foreach (var t in _allTables)
        {
            var tArea = string.IsNullOrWhiteSpace(t.AreaName) ? "OTHER" : t.AreaName;
            if (area == null || tArea == area)
            {
                Tables.Add(t);
            }
        }
    }

    /// <summary>Menu items matching the current search text, else the selected category tab.
    /// Exact code hits sort first so a scanned/typed code is always the top suggestion.</summary>
    private IList<MenuItem> FilterItems()
    {
        IEnumerable<MenuItem> src = _allItems;
        var q = SearchText?.Trim();
        if (!string.IsNullOrEmpty(q))
        {
            // Exact code matches come first, then sort by popularity (most sold → first).
            var pop = _popularityScores;
            var matches = src.Where(i => (i.Code ?? "").Equals(q, StringComparison.OrdinalIgnoreCase)
                                         || i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                         || (i.Code ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))
                             .OrderByDescending(i => (i.Code ?? "").Equals(q, StringComparison.OrdinalIgnoreCase))
                             .ThenByDescending(i => (i.Code ?? "").StartsWith(q, StringComparison.OrdinalIgnoreCase))
                             .ThenByDescending(i => pop.TryGetValue(i.Id, out var qty) ? qty : 0L);
            src = matches;
        }
        else if (SelectedCategoryTab?.Category != null)
        {
            // Match items filed directly under the category and those under it as a sub-category,
            // so picking a sub-category (Cold Coffee, Burger…) actually shows its items.
            var catId = SelectedCategoryTab.Category.Id;
            src = src.Where(i => i.CategoryId == catId || i.SubCategoryId == catId);
        }

        return src as IList<MenuItem> ?? src.ToList();
    }

    private void RefreshVisibleItems()
    {
        var list = FilterItems();
        Suggestions.ReplaceAll(list.Count > MaxSuggestions ? list.Take(MaxSuggestions) : list);

        // The full list only feeds the Menu view's item grid. Re-publishing it on every
        // keystroke while the Table view is up cost about a millisecond per matched item
        // for a list nobody could see — the single biggest source of search-box lag.
        // It's refreshed on switching to the Menu view instead.
        if (CenterMode == "Menu")
        {
            VisibleItems.ReplaceAll(list);
        }

        // Only pre-select while the user is actually searching. With an empty box the
        // suggestions are just the unfiltered menu and the dropdown is closed, so holding a
        // selection meant Enter silently added the alphabetically first menu item — adding
        // an item clears the box, so a second Enter kept adding that stray item.
        SelectedMenuItem = string.IsNullOrWhiteSpace(SearchText) ? null : Suggestions.FirstOrDefault();
    }

    partial void OnCenterModeChanged(string value)
    {
        if (value == "Menu")
        {
            VisibleItems.ReplaceAll(FilterItems());
        }
    }

    public void SelectNextMenuItem()
    {
        if (Suggestions.Count == 0) return;
        var idx = SelectedMenuItem != null ? Suggestions.IndexOf(SelectedMenuItem) : -1;
        if (idx < Suggestions.Count - 1)
        {
            SelectedMenuItem = Suggestions[idx + 1];
        }
    }

    public void SelectPreviousMenuItem()
    {
        if (Suggestions.Count == 0) return;
        var idx = SelectedMenuItem != null ? Suggestions.IndexOf(SelectedMenuItem) : -1;
        if (idx > 0)
        {
            SelectedMenuItem = Suggestions[idx - 1];
        }
    }

    public CartLine AddAndReturnLine(MenuItem item)
    {
        IsSearchPopupOpen = false;
        SearchText = "";
        if (HasExistingOrder)
        {
            SelectedCartTab = "New";
            OnPropertyChanged(nameof(DisplayCartItems));
            RefreshCartView();
        }

        var existing = Cart.FirstOrDefault(l => l.ItemId == item.Id && l.IsParcel == IsParcelMode && !l.IsSaved);
        if (existing != null)
        {
            existing.Qty++;
            RaiseTotals();
            return existing;
        }
        else
        {
            var line = AddLine(new CartLine { ItemId = item.Id, Name = item.Name, Price = item.Price, IsSaved = false, IsParcel = IsParcelMode });
            RaiseTotals();
            return line;
        }
    }

    partial void OnSelectedAreaChanged(AreaTab? value) => RefreshTables();
    partial void OnSelectedCategoryChanged(Category? value) => RefreshVisibleItems();

    partial void OnSelectedCategoryTabChanged(CategoryTabVM? value)
    {
        RefreshVisibleItems();
        // The best-sellers panel follows the same tab as the item grid.
        LoadPopularItems();
    }

    // True once the selected table already has a saved/running order ("OLD ORDER").
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewTableOrder))]
    private bool _hasExistingOrder;

    public bool IsNewTableOrder =>
        (BillMode == "Table" && !HasExistingOrder) ||
        (BillMode == "Table" && HasExistingOrder && SelectedCartTab == "New");

    /// <summary>
    /// Each working context keeps its own rung-but-not-yet-saved items, parked while a different
    /// one is being worked so nothing is lost or moved: a table's draft by table id, and the
    /// quick bill's in <see cref="_quickDraft"/>. Only a manual delete — or saving — takes items
    /// out; a draft is consumed the moment its context is reopened. Purely in memory, so unsaved
    /// work does not outlive the app, which is the same as any other unsaved order.
    /// </summary>
    private readonly Dictionary<long, List<CartLine>> _tableDrafts = new();
    private List<CartLine> _quickDraft = new();

    private static CartLine CopyLine(CartLine l) => new()
    {
        ItemId = l.ItemId, Name = l.Name, Price = l.Price, IsParcel = l.IsParcel, Qty = l.Qty, IsSaved = false
    };

    /// <summary>Parks a context's unsaved items — a table (by id), or the quick bill (null).</summary>
    private void StashDraft(TableView? context, List<CartLine> unsaved)
    {
        if (context is null)
        {
            _quickDraft = unsaved;
        }
        else if (unsaved.Count > 0)
        {
            _tableDrafts[context.Id] = unsaved;
        }
        else
        {
            _tableDrafts.Remove(context.Id);
        }
    }

    /// <summary>Takes back (and clears) a context's parked draft.</summary>
    private List<CartLine> TakeDraft(TableView? context)
    {
        if (context is null)
        {
            var quick = _quickDraft;
            _quickDraft = new List<CartLine>();
            return quick;
        }
        if (_tableDrafts.Remove(context.Id, out var draft))
        {
            return draft;
        }
        return new List<CartLine>();
    }

    /// <summary>While set, a table reload leaves the cart, tab and HasExistingOrder untouched.
    /// A KOT sets this around its LoadTables so the grid's amounts/times still refresh, but the
    /// cart it just built (and the tab it just moved to Old) aren't torn down and rebuilt — that
    /// rebuild, with its HasExistingOrder false→true bounce, was what left the tab header stale so
    /// the switch only "took" on the second KOT.</summary>
    private bool _suppressCartReload;

    partial void OnSelectedTableChanged(TableView? oldValue, TableView? newValue)
    {
        if (_suppressCartReload)
        {
            return;
        }

        // Only a real user switch parks and restores drafts. Two things must NOT:
        //  • the same table re-picked by a fresh object (LoadTables refreshing amount/status);
        //  • the null flicker LoadTables causes — clearing the Tables collection makes the list
        //    box drop its selection (→ this fires with a null table) before it is re-selected.
        // Treating those as switches would stash the current unsaved items and then restore them
        // on top of the freshly reloaded saved order, doubling every line just KOT'd.
        var sameTable = oldValue != null && newValue != null && oldValue.Id == newValue.Id;
        if (sameTable)
        {
            return;
        }
        var userSwitch = !sameTable && !_reloadingTables;

        if (userSwitch)
        {
            // Park the unsaved items of the context being left — a table's, or the quick bill's.
            // They belong to it; they are never carried onto the table being opened.
            StashDraft(oldValue, Cart.Where(l => !l.IsSaved).Select(CopyLine).ToList());
        }

        Cart.Clear();
        HasExistingOrder = false;
        SelectedCartTab = "Old";

        if (newValue != null)
        {
            var active = _orders.GetActiveOrderForTable(newValue.Id);
            if (active != null)
            {
                foreach (var it in active.Items)
                {
                    AddLine(new CartLine
                    {
                        ItemId = it.ItemId, Name = it.ItemName ?? "", Price = it.Price,
                        IsParcel = it.IsParcel != 0, Qty = it.Quantity, IsSaved = true
                    });
                }
                MergeSavedLines();
                HasExistingOrder = true;
            }
        }

        if (userSwitch)
        {
            // Bring back the newly-entered context's own parked draft. On a table that already has
            // a saved order these are additions, so they show in the "New" tab beside it.
            foreach (var l in TakeDraft(newValue))
            {
                if (HasExistingOrder) SelectedCartTab = "New";
                AddLine(l);
            }
        }

        if (newValue != null)
        {
            BillMode = "Table";
            CenterMode = "Table";
        }

        // The header reads the selected table's number, but it only refreshed when BillMode
        // flipped. Switching straight from one table to another leaves BillMode on "Table", so
        // without this the panel kept showing the first table's name for every table after it.
        OnPropertyChanged(nameof(BillTitle));
        RaiseTotals();
    }

    partial void OnBillModeChanged(string value)
    {
        // "Table" only means anything while a table is actually selected. Picking the tab
        // without one left the panel half in table mode — the table action buttons over a
        // bill with no table, while the header still read QUICK BILL — so it falls back to
        // the default Quick Bill tab. Choosing a table switches the mode by itself.
        if (value == "Table" && SelectedTable == null)
        {
            BillMode = "Quick";
            return;
        }

        OnPropertyChanged(nameof(BillTitle));
        if (value != "Table")
        {
            SelectedTable = null;
            HasExistingOrder = false;
        }
    }

    [RelayCommand] private void ShowTableView() => CenterMode = "Table";
    [RelayCommand] private void ShowMenuView() => CenterMode = "Menu";
    [RelayCommand] private void ShowOldTab() { SelectedCartTab = "Old"; OnPropertyChanged(nameof(DisplayCartItems)); RefreshCartView(); }
    [RelayCommand] private void ShowNewTab() { SelectedCartTab = "New"; OnPropertyChanged(nameof(DisplayCartItems)); RefreshCartView(); }
    [RelayCommand] private void SetBillMode(string mode) => BillMode = mode;

    public CartLine AddAndReturnLineCustom(CartLine line)
    {
        if (HasExistingOrder)
        {
            SelectedCartTab = "New";
            OnPropertyChanged(nameof(DisplayCartItems));
            RefreshCartView();
        }
        line.IsSaved = false;
        line.IsParcel = IsParcelMode;
        AddLine(line);
        RaiseTotals();
        return line;
    }

    [RelayCommand]
    private void AddItem(MenuItem item)
    {
        IsSearchPopupOpen = false;
        SearchText = "";
        if (HasExistingOrder)
        {
            SelectedCartTab = "New";
            OnPropertyChanged(nameof(DisplayCartItems));
            RefreshCartView();
        }

        var existing = Cart.FirstOrDefault(l => l.ItemId == item.Id && l.IsParcel == IsParcelMode && !l.IsSaved);
        if (existing != null)
        {
            existing.Qty++;
        }
        else
        {
            AddLine(new CartLine { ItemId = item.Id, Name = item.Name, Price = item.Price, IsSaved = false, IsParcel = IsParcelMode });
        }
        RaiseTotals();
    }

    [RelayCommand]
    private void Increment(CartLine line)
    {
        line.Qty++;
        RaiseTotals();
        if (line.IsSaved && BillMode == "Table" && SelectedTable != null && HasExistingOrder)
            PersistSavedOrder();
    }

    [RelayCommand]
    private void Decrement(CartLine line)
    {
        if (line.Qty > 1)
        {
            line.Qty--;
            RaiseTotals();
            if (line.IsSaved && BillMode == "Table" && SelectedTable != null && HasExistingOrder)
                PersistSavedOrder();
        }
        else
        {
            // Qty reached 0 — remove the line entirely (same as Remove command).
            var persist = line.IsSaved && BillMode == "Table" && SelectedTable != null && HasExistingOrder;
            Cart.Remove(line);
            RaiseTotals();
            if (persist)
                PersistSavedOrder();
        }
    }

    [RelayCommand]
    private void Remove(CartLine line)
    {
        // Deleting a line that belongs to a SAVED table order has to change the stored order too,
        // not just the on-screen cart — otherwise re-opening the table reloads it from the database
        // and the "deleted" item is back. A quick bill, or a not-yet-saved new line, only lives in
        // the cart, so those just drop out.
        var persist = line.IsSaved && BillMode == "Table" && SelectedTable != null && HasExistingOrder;
        Cart.Remove(line);
        RaiseTotals();
        if (persist)
        {
            PersistSavedOrder();
        }
    }

    [RelayCommand]
    private void ClearCart()
    {
        // On a table with a running order, "Clear All" empties the bill AND frees the table on the
        // floor — the order is deleted from the database, so the card goes back to available
        // instead of staying occupied with an order that only the screen had cleared.
        if (BillMode == "Table" && SelectedTable != null && HasExistingOrder)
        {
            DeleteTableOrder(SelectedTable.Id);
            return;
        }

        Cart.Clear();
        ClearDiscount();
        RaiseTotals();
    }

    /// <summary>
    /// Writes the cart's remaining SAVED lines back as the table's order, so an edit made by
    /// deleting a line sticks. When nothing saved is left the whole order is gone, so the table is
    /// freed. Reloads afterwards to keep the grid amount, the card colour and the cart in step.
    /// </summary>
    private void PersistSavedOrder()
    {
        if (SelectedTable == null)
        {
            return;
        }

        var saved = Cart.Where(l => l.IsSaved).ToList();
        if (saved.Count == 0)
        {
            DeleteTableOrder(SelectedTable.Id);
            return;
        }

        var payload = new TableOrderPayload { TableId = SelectedTable.Id, TableStatus = "ordered" };
        foreach (var l in saved)
        {
            payload.Items.Add(new OrderItemInput
            {
                ItemId = l.ItemId, ItemName = l.Name, Price = l.Price,
                Quantity = Math.Max(1, l.Qty), IsParcel = l.IsParcel
            });
        }
        var res = _orders.SaveTableOrder(payload);
        if (SelectedTable != null)
        {
            SelectedTable.Amount = res.TotalAmount;
        }
        RaiseTotals();
    }

    /// <summary>Public entry-point for code-behind to persist the table order after an
    /// in-place qty or price edit (the LostFocus handler can't reach the private method).</summary>
    public void PersistCartIfNeeded()
    {
        RaiseTotals();
        if (BillMode == "Table" && SelectedTable != null && HasExistingOrder
            && Cart.Any(l => l.IsSaved))
        {
            PersistSavedOrder();
        }
    }

    /// <summary>Cart lines to put on a KOT: only the unsaved (newly added) ones when the
    /// table already has an order, so the kitchen isn't re-sent items it already made.</summary>
    private List<Pos.Core.Printing.PrintLine> KotLines()
    {
        var source = Cart.Any(l => !l.IsSaved) ? Cart.Where(l => !l.IsSaved) : Cart;
        return source.Select(l => new Pos.Core.Printing.PrintLine(l.Name, Math.Max(1, l.Qty), l.Price, l.IsParcel)).ToList();
    }

    private List<Pos.Core.Printing.PrintLine> BillLines() =>
        Cart.Select(l => new Pos.Core.Printing.PrintLine(l.Name, Math.Max(1, l.Qty), l.Price, l.IsParcel)).ToList();

    [RelayCommand]
    public void PrintKot()
    {
        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pos_app_debug.log"), 
            $"[DEBUG {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] PrintKot entered. Cart: {Cart.Count}, BillMode: {BillMode}, SelectedTable: {SelectedTable?.TableNumber}\r\n");
        if (Cart.Count == 0) return;

        // Built before saving, so "new items only" is evaluated pre-merge.
        var lines = KotLines();
        var tableLabel = BillMode == "Table" && SelectedTable != null ? $"T-{SelectedTable.TableNumber}" : "Quick";
        var cfg = Settings.BuildPrintConfig();
        var ticket = new Pos.Core.Printing.ReceiptBuilder(cfg).BuildKot(lines, tableLabel, null);

        if (BillMode == "Table" && SelectedTable != null)
        {
            try
            {
                var payload = BuildPayload("ordered");
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pos_app_debug.log"), 
                    $"[DEBUG] PrintKot Payload built. Items count: {payload.Items.Count}, Total: {payload.TotalAmount}\r\n");
                var res = _orders.SaveTableOrder(payload);
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pos_app_debug.log"), 
                    $"[DEBUG] PrintKot SaveTableOrder returned. Success: {res.Success}, TotalAmount: {res.TotalAmount}\r\n");

                // Paper starts the moment the order is safely in SQLite — reloading the tables and
                // redrawing the cart below is the counter catching up, and the kitchen should not
                // be waiting behind it.
                _printer.Enqueue("KOT", cfg, ticket);

                // Refresh the grid's amounts/times, but suppress the cart rebuild — the cart already
                // holds the full order in memory, and letting the reload tear it down is what left the
                // tab header stale. ShowMergedOldOrder then does the clean New→Old switch.
                _suppressCartReload = true;
                try { LoadTables(); }
                finally { _suppressCartReload = false; }
                ShowMergedOldOrder();
                StatusMessage($"KOT — Table {SelectedTable.TableNumber}, Total: ₹{res.TotalAmount:0.##}");
            }
            catch (System.Exception ex)
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pos_app_debug.log"), 
                    $"[DEBUG] Exception in PrintKot: {ex}\r\n");
                throw;
            }
        }
        else
        {
            // A quick bill has no table to hold a running order, so printing its KOT is the
            // whole sale: it is recorded as settled and the counter cleared. Otherwise the
            // kitchen would have a ticket the day's takings knew nothing about.
            var res = RecordQuickBill();
            _printer.Enqueue("KOT", cfg, ticket);
            ClearCounter();
            LoadPopularItems();

            StatusMessage($"KOT — Quick Bill {res.FormattedBillNumber}, Total: ₹{res.TotalAmount:0.##}");
        }
    }

    /// <summary>
    /// Writes a quick bill as settled. Every quick-bill button records the sale through here,
    /// so it can only ever be written once however the operator finishes it. Clearing the
    /// counter is <see cref="ClearCounter"/>'s job — printing goes in between the two, so
    /// paper is on its way before the screen redraws.
    /// </summary>
    private SaveOrderResult RecordQuickBill() => _orders.SaveFinalOrder(BuildPayload("completed"));

    /// <summary>Empties the counter for the next customer.</summary>
    private void ClearCounter()
    {
        Cart.Clear();
        ClearDiscount();
        ClearEditingNoteState();
        RaiseTotals();
    }

    private static void ShowPrintError(string what, string error) =>
        Views.ThemeMessageBox.Show($"{what} print nahi ho paya:\n\n{error}", "Print Error", "error");

    [RelayCommand]
    public void SaveKot()
    {
        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pos_app_debug.log"), 
            $"[DEBUG {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] SaveKot entered. Cart: {Cart.Count}, BillMode: {BillMode}, SelectedTable: {SelectedTable?.TableNumber}\r\n");
        if (Cart.Count == 0) return;
        if (BillMode == "Table" && SelectedTable != null)
        {
            try
            {
                var payload = BuildPayload("ordered");
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pos_app_debug.log"), 
                    $"[DEBUG] Payload built. Items count: {payload.Items.Count}, Total: {payload.TotalAmount}\r\n");
                foreach (var i in payload.Items)
                {
                    System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pos_app_debug.log"), 
                        $"[DEBUG] Payload Item: {i.ItemId}/{i.ItemName}, Qty: {i.Quantity}, Price: {i.Price}\r\n");
                }
                var res = _orders.SaveTableOrder(payload);
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pos_app_debug.log"), 
                    $"[DEBUG] SaveTableOrder returned. Success: {res.Success}, TotalAmount: {res.TotalAmount}\r\n");
                _suppressCartReload = true;
                try { LoadTables(); }
                finally { _suppressCartReload = false; }
                ShowMergedOldOrder();
                StatusMessage($"KOT Saved — Table {SelectedTable.TableNumber}, Total: ₹{res.TotalAmount:0.##}");
            }
            catch (System.Exception ex)
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pos_app_debug.log"), 
                    $"[DEBUG] Exception in SaveKot: {ex}\r\n");
                throw;
            }
        }
    }

    [RelayCommand]
    public void PrintBill()
    {
        if (Cart.Count == 0) return;

        // Captured before anything is cleared — the printed bill must show what was sold.
        var lines = BillLines();
        var total = GrandTotal;
        var discount = DiscountAmount;
        var cfg = Settings.BuildPrintConfig();
        var builder = new Pos.Core.Printing.ReceiptBuilder(cfg);

        if (BillMode == "Table" && SelectedTable != null)
        {
            var payload = BuildPayload("ordered");
            payload.AssignBillNumber = true;
            var res = _orders.SaveTableOrder(payload);
            var bill = builder.BuildBill(lines, res.FormattedBillNumber, SelectedTable.TableNumber,
                                         discount, total, DateTime.Now);

            // Queued the instant the bill has its number: everything below is the counter
            // redrawing itself, and the customer should not be watching paper wait for it.
            // The grand total rides along so the bill's UPI QR opens the customer's app with the
            // amount already filled in.
            _printer.Enqueue("Bill", cfg, bill, withQr: true, qrAmount: total);

            // Refresh the grid's amounts/times, but suppress the cart rebuild — the cart already
            // holds the full order in memory, and letting the reload tear it down is what left the
            // tab header stale. ShowMergedOldOrder then does the clean New→Old switch.
            _suppressCartReload = true;
            try { LoadTables(); }
            finally { _suppressCartReload = false; }
            ShowMergedOldOrder();
            StatusMessage($"Bill — Table {SelectedTable.TableNumber}, Bill {res.FormattedBillNumber}");
        }
        else
        {
            // Quick bill: the kitchen ticket goes out alongside the customer's bill, because
            // nothing was sent earlier — there is no running table order behind it.
            var ticket = builder.BuildKot(KotLines(), "Quick", null);
            var res = RecordQuickBill();
            var bill = builder.BuildBill(lines, res.FormattedBillNumber, "", discount, total, DateTime.Now);

            _printer.Enqueue("KOT", cfg, ticket);
            _printer.Enqueue("Bill", cfg, bill, withQr: true, qrAmount: total);

            ClearCounter();
            StatusMessage($"KOT + Bill — Quick Bill {res.FormattedBillNumber}");
        }

        LoadPopularItems();
    }

    /// <summary>
    /// Reprints a settled bill from the Reports log — the duplicate copy a customer asks for
    /// after the paper is gone.
    ///
    /// It goes through the same builder, config and spooler as the original print, so the
    /// duplicate reads identically instead of being a second, differently-formatted document.
    /// Nothing is written back: a reprint must not touch the day's takings or the bill number.
    /// </summary>
    public void ReprintBill(Order order, IReadOnlyList<OrderItem> items)
    {
        if (items.Count == 0) return;

        var cfg = Settings.BuildPrintConfig();
        var lines = items
            .Select(i => new Pos.Core.Printing.PrintLine(
                i.ItemName ?? "", Math.Max(1, i.Quantity), i.Price, i.IsParcel != 0))
            .ToList();

        // The bill carries the time it was originally billed, not the time of the reprint.
        var billedAt = DateTime.TryParse(order.BilledAt ?? order.CreatedAt, out var d) ? d : DateTime.Now;

        var bill = new Pos.Core.Printing.ReceiptBuilder(cfg).BuildBill(
            lines, _orders.FormatBillNumber(order.BillNumber), order.TableNumber ?? "",
            order.DiscountAmount, order.TotalAmount, billedAt);

        _printer.Enqueue("Bill", cfg, bill, withQr: true, qrAmount: order.TotalAmount);
    }

    [RelayCommand]
    public void SettleBill()
    {
        SettleOrder();
    }

    /// <summary>
    /// Closes the bill and clears the counter — no dialog, no confirmation. Payment mode is
    /// deliberately not asked for or stored (orders has no column for it, and the Electron
    /// app doesn't keep it either); putting a bill on a customer's khata is a separate job
    /// done later from the reports screen.
    /// </summary>
    public void SettleOrder()
    {
        if (Cart.Count == 0) return;

        if (BillMode == "Table" && SelectedTable != null)
        {
            // One push carrying the items, not a bill followed by an empty "clear".
            // "available" + a full bill is how both the local repository and the server
            // read "settled, table free"; the empty clear only freed the table on the
            // server and left the bill sitting there as completed for ever.
            _orders.SaveTableOrder(BuildPayload("available"));
        }
        else
        {
            // A quick bill belongs to no table. Saving it through the table-order path made
            // the repository resolve the missing table id to the first table, so every quick
            // settle landed on Table 1 and left it looking occupied.
            _orders.SaveFinalOrder(BuildPayload("completed"));
        }

        Cart.Clear();
        ClearDiscount();
        ClearEditingNoteState();
        LoadTables();
        SelectedTable = null;
        BillMode = "Quick";
        CenterMode = "Table";
        RaiseTotals();
        LoadPopularItems();
        StatusMessage("Bill Settled & Table Freed.");
    }

    public void TransferTableOrder(long targetTableId)
    {
        if (Cart.Count == 0) return;

        var items = new List<OrderItemInput>();
        foreach (var l in Cart)
        {
            items.Add(new OrderItemInput
            {
                ItemId = l.ItemId, ItemName = l.Name, Price = l.Price,
                Quantity = Math.Max(1, l.Qty), IsParcel = l.IsParcel
            });
        }

        // Carry the original order-start time to the new table. The target table is empty,
        // so without this the repository falls back to "now" and the elapsed clock restarts
        // — moving a table must not make an old order look freshly placed.
        var originalTimestamp = BillMode == "Table" ? SelectedTable?.OrderTimestamp : null;

        if (BillMode == "Table" && SelectedTable != null)
        {
            _orders.SaveTableOrder(new TableOrderPayload { TableId = SelectedTable.Id, TableStatus = "available", Items = new() });
        }

        _orders.SaveTableOrder(new TableOrderPayload
        {
            TableId = targetTableId,
            TableStatus = "ordered",
            Items = items,
            OrderTimestamp = originalTimestamp
        });

        Cart.Clear();
        LoadTables();
        BillMode = "Table";
        CenterMode = "Table";
        SelectedTable = Tables.FirstOrDefault(t => t.Id == targetTableId);
    }

    public void MergeTableOrder(long sourceTableId, long targetTableId)
    {
        var sourceOrder = _orders.GetActiveOrderForTable(sourceTableId);
        var targetOrder = _orders.GetActiveOrderForTable(targetTableId);

        var mergedItems = new List<OrderItemInput>();
        if (targetOrder?.Items != null)
        {
            foreach (var item in targetOrder.Items)
            {
                mergedItems.Add(new OrderItemInput
                {
                    ItemId = item.ItemId, ItemName = item.ItemName, Price = item.Price,
                    Quantity = item.Quantity, IsParcel = item.IsParcel != 0
                });
            }
        }

        if (sourceOrder?.Items != null)
        {
            foreach (var item in sourceOrder.Items)
            {
                bool itemIsParcel = item.IsParcel != 0;
                var existing = mergedItems.FirstOrDefault(i => i.ItemId == item.ItemId && i.IsParcel == itemIsParcel);
                if (existing != null)
                {
                    existing.Quantity += item.Quantity;
                }
                else
                {
                    mergedItems.Add(new OrderItemInput
                    {
                        ItemId = item.ItemId, ItemName = item.ItemName, Price = item.Price,
                        Quantity = item.Quantity, IsParcel = itemIsParcel
                    });
                }
            }
        }

        _orders.SaveTableOrder(new TableOrderPayload { TableId = sourceTableId, TableStatus = "available", Items = new() });
        _orders.SaveTableOrder(new TableOrderPayload { TableId = targetTableId, TableStatus = "ordered", Items = mergedItems });

        LoadTables();
        SelectedTable = Tables.FirstOrDefault(t => t.Id == targetTableId);
    }

    public void SplitTableOrder(long targetTableId, List<CartLine> linesToSplit)
    {
        if (linesToSplit.Count == 0 || SelectedTable == null) return;

        var splitItems = new List<OrderItemInput>();
        foreach (var l in linesToSplit)
        {
            splitItems.Add(new OrderItemInput
            {
                ItemId = l.ItemId, ItemName = l.Name, Price = l.Price,
                Quantity = Math.Max(1, l.Qty), IsParcel = l.IsParcel
            });
            Cart.Remove(l);
        }

        var remainingItems = new List<OrderItemInput>();
        foreach (var l in Cart)
        {
            remainingItems.Add(new OrderItemInput
            {
                ItemId = l.ItemId, ItemName = l.Name, Price = l.Price,
                Quantity = Math.Max(1, l.Qty), IsParcel = l.IsParcel
            });
        }

        // The split-off items belong to the same sitting, so the new table inherits the
        // original order-start time rather than starting a fresh clock.
        var originalTimestamp = SelectedTable.OrderTimestamp;

        var currentTableStatus = remainingItems.Count > 0 ? "ordered" : "available";
        _orders.SaveTableOrder(new TableOrderPayload { TableId = SelectedTable.Id, TableStatus = currentTableStatus, Items = remainingItems });
        _orders.SaveTableOrder(new TableOrderPayload
        {
            TableId = targetTableId,
            TableStatus = "ordered",
            Items = splitItems,
            OrderTimestamp = originalTimestamp
        });

        LoadTables();
        SelectedTable = Tables.FirstOrDefault(t => t.Id == SelectedTable.Id);
    }

    public QuickNotesRepository QuickNotesRepo => _quickNotes;

    [ObservableProperty] private bool _hasSavedNotes;
    [ObservableProperty] private long? _editingNoteId;
    [ObservableProperty] private string _editingCustomerName = "";
    [ObservableProperty] private string _editingCustomerMobile = "";
    [ObservableProperty] private string _editingTargetTime = "";

    public void ClearEditingNoteState()
    {
        EditingNoteId = null;
        EditingCustomerName = "";
        EditingCustomerMobile = "";
        EditingTargetTime = "";
    }

    public void RefreshSavedNotesState()
    {
        HasSavedNotes = _quickNotes.GetNotes().Count > 0;
    }

    public string SaveCurrentOrderToNote(string customerName, string customerMobile, string targetTime)
    {
        if (Cart.Count == 0) return "";

        var existingNotes = _quickNotes.GetNotes();
        var defaultName = string.IsNullOrWhiteSpace(customerName)
            ? (EditingNoteId != null && !string.IsNullOrWhiteSpace(EditingCustomerName) ? EditingCustomerName : $"Quick Order #{existingNotes.Count + 1}")
            : customerName.Trim();

        var noteItems = new List<QuickNoteItem>();
        foreach (var l in Cart)
        {
            noteItems.Add(new QuickNoteItem
            {
                ItemId = l.ItemId ?? 0,
                Name = l.Name,
                Price = l.Price,
                Qty = (int)Math.Max(1, l.Qty),
                IsParcel = l.IsParcel
            });
        }

        var json = System.Text.Json.JsonSerializer.Serialize(noteItems);
        var note = new QuickNote
        {
            Id = EditingNoteId ?? 0,
            CustomerName = defaultName,
            CustomerMobile = (customerMobile ?? "").Trim(),
            SavedTime = DateTime.Now.ToString("hh:mm tt"),
            TargetTime = string.IsNullOrWhiteSpace(targetTime) ? DateTime.Now.ToString("hh:mm tt") : targetTime.Trim(),
            TotalQty = Convert.ToInt32(TotalQty),
            GrandTotal = GrandTotal,
            ItemsJson = json
        };

        _quickNotes.SaveNote(note);
        ClearEditingNoteState();
        Cart.Clear();
        RaiseTotals();
        RefreshSavedNotesState();
        Notes.LoadQuickNotes();
        return defaultName;
    }

    public void LoadNoteToCart(QuickNote note)
    {
        Cart.Clear();
        EditingNoteId = note.Id;
        EditingCustomerName = note.CustomerName;
        EditingCustomerMobile = note.CustomerMobile;
        EditingTargetTime = note.TargetTime;

        try
        {
            var items = System.Text.Json.JsonSerializer.Deserialize<List<QuickNoteItem>>(note.ItemsJson);
            if (items != null)
            {
                foreach (var i in items)
                {
                    AddLine(new CartLine
                    {
                        ItemId = i.ItemId,
                        Name = i.Name,
                        Price = i.Price,
                        Qty = Math.Max(1, i.Qty),
                        IsParcel = i.IsParcel,
                        IsSaved = false
                    });
                }
            }
        }
        catch { }

        BillMode = "Quick";
        SelectedTable = null;
        RaiseTotals();
        RefreshSavedNotesState();
    }

    public void TransferNoteToTable(QuickNote note, long targetTableId)
    {
        var items = new List<OrderItemInput>();
        try
        {
            var noteItems = System.Text.Json.JsonSerializer.Deserialize<List<QuickNoteItem>>(note.ItemsJson);
            if (noteItems != null)
            {
                foreach (var i in noteItems)
                {
                    items.Add(new OrderItemInput
                    {
                        ItemId = i.ItemId,
                        ItemName = i.Name,
                        Price = i.Price,
                        Quantity = Math.Max(1, i.Qty),
                        IsParcel = i.IsParcel
                    });
                }
            }
        }
        catch { }

        _orders.SaveTableOrder(new TableOrderPayload
        {
            TableId = targetTableId,
            TableStatus = "ordered",
            Items = items,
            CustomerName = note.CustomerName,
            CustomerMobile = note.CustomerMobile
        });

        _quickNotes.DeleteNote(note.Id);
        if (EditingNoteId == note.Id)
        {
            ClearEditingNoteState();
        }
        LoadTables();
        RaiseTotals();
        RefreshSavedNotesState();
        Notes.LoadQuickNotes();
    }

    public void DeleteQuickNote(long noteId)
    {
        _quickNotes.DeleteNote(noteId);
        RefreshSavedNotesState();
    }

    public void DeleteTableOrder(long tableId)
    {
        _orders.SaveTableOrder(new TableOrderPayload { TableId = tableId, TableStatus = "available", Items = new() });
        Cart.Clear();
        LoadTables();
        SelectedTable = null;
        BillMode = "Quick";
        CenterMode = "Table";
        RaiseTotals();
    }

    private CartLine AddLine(CartLine line)
    {
        line.PropertyChanged += (_, _) => RaiseTotals();
        Cart.Add(line);
        return line;
    }

    private TableOrderPayload BuildPayload(string status)
    {
        // Null, not 0, when there's no table: a quick bill's order row keeps table_id NULL.
        var payload = new TableOrderPayload
        {
            TableId = SelectedTable?.Id,
            TableStatus = status,
            // Without these the row stored the raw line sum and no discount at all, so a
            // discounted bill printed one number and reported another.
            TotalAmount = GrandTotal,
            DiscountAmount = DiscountAmount,
            DiscountType = HasDiscount ? DiscountType : null,
            DiscountValue = DiscountValue,
            DiscountLabel = HasDiscount ? DiscountLabel : null,
            IsParcelMode = IsParcelMode
        };
        var grouped = Cart.GroupBy(l => new
        {
            Key = l.ItemId.HasValue 
                ? l.ItemId.Value.ToString() 
                : (l.Name.ToLowerInvariant().Trim() + "|" + l.Price.ToString("0.00")),
            l.IsParcel
        });
        foreach (var g in grouped)
        {
            var first = g.First();
            payload.Items.Add(new OrderItemInput
            {
                ItemId = first.ItemId,
                ItemName = first.Name,
                Price = first.Price,
                Quantity = Math.Max(1, g.Sum(l => l.Qty)),
                IsParcel = g.Key.IsParcel
            });
        }
        return payload;
    }

    private void StatusMessage(string _) { /* reserved for a toast; no-op for now */ }

    private void RaiseTotals()
    {
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(DiscountAmount));
        OnPropertyChanged(nameof(DiscountLabel));
        OnPropertyChanged(nameof(HasDiscount));
        OnPropertyChanged(nameof(GrandTotal));
        OnPropertyChanged(nameof(TotalQty));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(OldOrderItems));
        OnPropertyChanged(nameof(NewOrderItems));
        OnPropertyChanged(nameof(DisplayCartItems));
        OnPropertyChanged(nameof(IsCartPanelVisible));
        // Deliberately no DisplayCartItemsView notification and no view Refresh here:
        // the view is cached with live filtering/grouping, so it tracks IsSaved/IsParcel
        // by itself. Refreshing on every total recalculation was regrouping the whole
        // cart, which reset selection and scroll position mid-order.

        if (BillMode == "Table" && SelectedTable != null)
        {
            SelectedTable.Amount = GrandTotal;
        }
    }

    private void MergeSavedLines()
    {
        var grouped = Cart.Where(l => l.IsSaved)
                          .GroupBy(l => new
                          {
                              Key = l.ItemId.HasValue 
                                  ? l.ItemId.Value.ToString() 
                                  : (l.Name.ToLowerInvariant().Trim() + "|" + l.Price.ToString("0.00")),
                              l.IsParcel
                          })
                          .ToList();

        foreach (var group in grouped)
        {
            if (group.Count() > 1)
            {
                var first = group.First();
                first.Qty = group.Sum(l => l.Qty);
                foreach (var dup in group.Skip(1))
                {
                    Cart.Remove(dup);
                }
            }
        }
    }
}

public partial class CategoryTabVM : ObservableObject
{
    public Category? Category { get; set; }
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public string DisplayText => $"{Name.ToUpperInvariant()} ({Count})";
}
