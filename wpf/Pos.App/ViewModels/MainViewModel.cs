using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.App.Helpers;
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

    /// <summary>Receipts go out through here, never on the UI thread — see PrintSpooler.</summary>
    private readonly Services.PrintSpooler _printer = new();

    private List<MenuItem> _allItems = new();
    private List<TableView> _allTables = new();

    public ObservableCollection<AreaTab> Areas { get; } = new();
    public ObservableCollection<TableView> Tables { get; } = new();     // filtered by area
    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<CategoryTabVM> CategoryTabs { get; } = new();
    public FastObservableCollection<MenuItem> VisibleItems { get; } = new();

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
    /// The shop's name in the sidebar, from the saved profile — not a constant. Renaming the
    /// shop in Settings used to change the bill but leave the sidebar reading the old name.
    /// </summary>
    public string ClientName => Settings.StoreName is { Length: > 0 } name ? name : "POS";

    /// <summary>The two letters on the round logo, taken from the same name.</summary>
    public string ClientInitials => Settings.StoreInitials;

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

    public MainViewModel(MenuRepository menu, TableRepository tables, OrderRepository orders, QuickNotesRepository quickNotes, SyncCoordinator sync, LedgerViewModel ledger, SettingsViewModel settings, ReportsViewModel reports, NotesViewModel notes, QrOrderViewModel qr)
    {
        _menu = menu;
        _tables = tables;
        _orders = orders;
        _quickNotes = quickNotes;
        _sync = sync;
        Ledger = ledger;
        Settings = settings;
        Reports = reports;
        Notes = notes;
        Qr = qr;

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

        _printer.Failed += (what, error) =>
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(new Action(() => ShowPrintError(what, error)));
            }
        };

        _sync.StatusChanged += OnSyncStatusChanged;
        ShowSync(_sync.Status);
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

    private void LoadCatalog()
    {
        _allItems = _menu.GetMenuItems().ToList();
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
            var cnt = _allItems.Count(i => i.CategoryId == c.Id);
            CategoryTabs.Add(new CategoryTabVM
            {
                Category = c,
                Name = c.Name,
                Count = cnt
            });
        }
        SelectedCategoryTab = CategoryTabs.FirstOrDefault();
    }

    private void LoadTables()
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
            var matches = src.Where(i => (i.Code ?? "").Equals(q, StringComparison.OrdinalIgnoreCase)
                                         || i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                         || (i.Code ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))
                             .OrderByDescending(i => (i.Code ?? "").Equals(q, StringComparison.OrdinalIgnoreCase))
                             .ThenByDescending(i => (i.Code ?? "").StartsWith(q, StringComparison.OrdinalIgnoreCase));
            src = matches;
        }
        else if (SelectedCategoryTab?.Category != null)
        {
            src = src.Where(i => i.CategoryId == SelectedCategoryTab.Category.Id);
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
    partial void OnSelectedCategoryTabChanged(CategoryTabVM? value) => RefreshVisibleItems();

    // True once the selected table already has a saved/running order ("OLD ORDER").
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewTableOrder))]
    private bool _hasExistingOrder;

    public bool IsNewTableOrder =>
        (BillMode == "Table" && !HasExistingOrder) ||
        (BillMode == "Table" && HasExistingOrder && SelectedCartTab == "New");

    partial void OnSelectedTableChanged(TableView? value)
    {
        Cart.Clear();
        HasExistingOrder = false;
        SelectedCartTab = "Old";
        if (value != null)
        {
            var active = _orders.GetActiveOrderForTable(value.Id);
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
                HasExistingOrder = true;
            }
            BillMode = "Table";
            CenterMode = "Table";
        }
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

    [RelayCommand] private void Increment(CartLine line) => line.Qty++;

    [RelayCommand]
    private void Decrement(CartLine line)
    {
        if (line.Qty > 1) { line.Qty--; }
        else { Cart.Remove(line); }
    }

    [RelayCommand] private void Remove(CartLine line) => Cart.Remove(line);

    [RelayCommand]
    private void ClearCart()
    {
        Cart.Clear();
        ClearDiscount();
        RaiseTotals();
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
        if (Cart.Count == 0) return;

        // Built before saving, so "new items only" is evaluated pre-merge.
        var lines = KotLines();
        var tableLabel = BillMode == "Table" && SelectedTable != null ? $"T-{SelectedTable.TableNumber}" : "Quick";
        var cfg = Settings.BuildPrintConfig();
        var ticket = new Pos.Core.Printing.ReceiptBuilder(cfg).BuildKot(lines, tableLabel, null);

        if (BillMode == "Table" && SelectedTable != null)
        {
            var res = _orders.SaveTableOrder(BuildPayload("ordered"));
            LoadTables();
            HasExistingOrder = true;
            foreach (var l in Cart)
            {
                l.IsSaved = true;
            }
            MergeSavedLines();
            SelectedCartTab = "Old";
            RaiseTotals();
            StatusMessage($"KOT — Table {SelectedTable.TableNumber}, Total: ₹{res.TotalAmount:0.##}");
        }
        else
        {
            // A quick bill has no table to hold a running order, so printing its KOT is the
            // whole sale: it is recorded as settled and the counter cleared. Otherwise the
            // kitchen would have a ticket the day's takings knew nothing about.
            var res = FinishQuickBill();
            StatusMessage($"KOT — Quick Bill #{res.BillNumber}, Total: ₹{res.TotalAmount:0.##}");
        }

        _printer.Enqueue("KOT", cfg, ticket);
    }

    /// <summary>
    /// Closes a quick bill: records it as settled and clears the counter for the next
    /// customer. Every quick-bill button ends here, so a sale can only ever be written once
    /// however the operator finishes it.
    /// </summary>
    private SaveOrderResult FinishQuickBill()
    {
        var result = _orders.SaveFinalOrder(BuildPayload("completed"));
        Cart.Clear();
        ClearDiscount();
        ClearEditingNoteState();
        RaiseTotals();
        return result;
    }

    private static void ShowPrintError(string what, string error) =>
        System.Windows.MessageBox.Show($"{what} print nahi ho paya:\n\n{error}", "Print Error",
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);

    [RelayCommand]
    public void SaveKot()
    {
        if (Cart.Count == 0) return;
        if (BillMode == "Table" && SelectedTable != null)
        {
            var res = _orders.SaveTableOrder(BuildPayload("ordered"));
            LoadTables();
            HasExistingOrder = true;
            foreach (var l in Cart)
            {
                l.IsSaved = true;
            }
            MergeSavedLines();
            SelectedCartTab = "Old";
            RaiseTotals();
            StatusMessage($"KOT Saved — Table {SelectedTable.TableNumber}, Total: ₹{res.TotalAmount:0.##}");
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
            var res = _orders.SaveTableOrder(BuildPayload("completed"));
            var bill = builder.BuildBill(lines, res.BillNumber?.ToString() ?? "", SelectedTable.TableNumber,
                                         discount, total, DateTime.Now);

            LoadTables();
            foreach (var l in Cart)
            {
                l.IsSaved = true;
            }
            MergeSavedLines();
            SelectedCartTab = "Old";
            RaiseTotals();
            StatusMessage($"Bill — Table {SelectedTable.TableNumber}, Bill #{res.BillNumber}");
            _printer.Enqueue("Bill", cfg, bill, withQr: true);
        }
        else
        {
            // Quick bill: the kitchen ticket goes out alongside the customer's bill, because
            // nothing was sent earlier — there is no running table order behind it.
            var ticket = builder.BuildKot(KotLines(), "Quick", null);
            var res = FinishQuickBill();
            var bill = builder.BuildBill(lines, res.BillNumber?.ToString() ?? "", "", discount, total, DateTime.Now);

            StatusMessage($"KOT + Bill — Quick Bill #{res.BillNumber}");
            _printer.Enqueue("KOT", cfg, ticket);
            _printer.Enqueue("Bill", cfg, bill, withQr: true);
        }
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
        foreach (var l in Cart)
        {
            payload.Items.Add(new OrderItemInput
            {
                ItemId = l.ItemId, ItemName = l.Name, Price = l.Price,
                Quantity = Math.Max(1, l.Qty), IsParcel = l.IsParcel
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
                          .GroupBy(l => new { l.ItemId, l.IsParcel })
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
