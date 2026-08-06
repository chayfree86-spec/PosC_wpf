using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Data;
using Pos.Core.Models;
using Pos.Core.Repositories;
using Pos.Core.Sync;

namespace Pos.App.ViewModels;

public partial class ReportsViewModel : ObservableObject
{
    private const int PageSize = 12;

    /// <summary>The "every business on the till" row. Null id is what the queries read as
    /// "don't filter by client".</summary>
    private static readonly ReportCounter AllCounters = new() { Id = null, Name = "All (Sabhi Counter)", Slug = "" };

    private readonly ReportsRepository _repo;
    private readonly ClientContext _client;
    private readonly CustomerLedgerRepository _ledgerRepo;
    private readonly SyncCoordinator _sync;
    private readonly List<ReportRow> _all = new();       // full result for the range
    private List<ReportRow> _filtered = new();           // after search

    /// <summary>Each business's bill prefix, worked out once and reused — in "All" mode the log
    /// mixes brands, and every row has to carry its own shop's letters (#DR-… vs #CC-…).</summary>
    private readonly Dictionary<long, string> _prefixByClient = new();
    private bool _suspendReload;                         // while the counter list is rebuilt

    public ObservableCollection<ReportRow> Orders { get; } = new();   // current page

    /// <summary>The category-wise breakdown, one group per category, each holding its items.</summary>
    public ObservableCollection<CategorySalesGroup> CategorySales { get; } = new();

    /// <summary>The full, unfiltered item rollup for the period; the category tab's filters are
    /// applied over this so switching a dropdown never needs another fetch.</summary>
    private IReadOnlyList<CategoryItemSale> _categoryRaw = new List<CategoryItemSale>();

    /// <summary>Dropdown options for the category tab. "All" plus every category / sub-category
    /// present in the period; the sub-category list narrows to the picked category.</summary>
    public ObservableCollection<string> CategoryFilterOptions { get; } = new();
    public ObservableCollection<string> SubCategoryFilterOptions { get; } = new();

    /// <summary>"All" plus each business on the till, for the counter filter.</summary>
    public ObservableCollection<ReportCounter> Counters { get; } = new();

    [ObservableProperty] private string _rangeType = "today";
    [ObservableProperty] private string _startDate = "";
    [ObservableProperty] private string _endDate = "";
    [ObservableProperty] private string _searchTerm = "";
    [ObservableProperty] private string _totalRevenue = "₹0";
    [ObservableProperty] private string _totalBills = "0";
    [ObservableProperty] private string _totalDiscounts = "₹0";
    [ObservableProperty] private string _exportMessage = "";
    [ObservableProperty] private bool _isCustomRange;
    [ObservableProperty] private DateTime _customStart = DateTime.Today;
    [ObservableProperty] private DateTime _customEnd = DateTime.Today;
    [ObservableProperty] private int _currentPage;
    [ObservableProperty] private ReportCounter? _selectedCounter;

    /// <summary>Which tab of the log panel is showing: false = Order Log, true = category-wise
    /// items. <see cref="ShowOrderLog"/> is the inverse so each panel can bind its own visibility
    /// without an inverting converter.</summary>
    [ObservableProperty] private bool _showCategoryView;

    public bool ShowOrderLog => !ShowCategoryView;
    partial void OnShowCategoryViewChanged(bool value) => OnPropertyChanged(nameof(ShowOrderLog));

    private const string AllFilter = "All";

    /// <summary>The category tab's own filters — a category, a sub-category within it, and a name
    /// search — applied together over <see cref="_categoryRaw"/>.</summary>
    [ObservableProperty] private string _selectedCategoryFilter = AllFilter;
    [ObservableProperty] private string _selectedSubCategoryFilter = AllFilter;
    [ObservableProperty] private string _categorySearch = "";

    partial void OnSelectedCategoryFilterChanged(string value)
    {
        RebuildSubCategoryOptions();
        RebuildCategoryGroups();
    }

    partial void OnSelectedSubCategoryFilterChanged(string value) => RebuildCategoryGroups();
    partial void OnCategorySearchChanged(string value) => RebuildCategoryGroups();

    /// <summary>Header shown above the category breakdown — how many item lines it covers.</summary>
    public string CategoryCountText => $"{CategorySales.Count} categories";
    public bool HasCategorySales => CategorySales.Count > 0;
    public bool HasNoCategorySales => CategorySales.Count == 0;

    [RelayCommand] private void SelectOrderLog() => ShowCategoryView = false;
    [RelayCommand] private void SelectCategoryView() => ShowCategoryView = true;

    public string PeriodDisplay => $"{Fmt(StartDate)}   to   {Fmt(EndDate)}";
    public int OrderCount => _filtered.Count;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(_filtered.Count / (double)PageSize));
    public string PageInfo => $"Page {CurrentPage + 1} of {TotalPages}";

    public ReportsViewModel(ReportsRepository repo, CustomerLedgerRepository ledgerRepo, ClientContext client, SyncCoordinator sync)
    {
        _repo = repo;
        _ledgerRepo = ledgerRepo;
        _client = client;
        _sync = sync;
        _suspendReload = true;
        LoadCounters();
        _suspendReload = false;
        SetRange("today");
    }

    /// <summary>The customers the "Add to Khata" dialog offers to pick from.</summary>
    public IReadOnlyList<Customer> GetLedgerCustomers() => _ledgerRepo.GetCustomers().ToList();

    /// <summary>
    /// Files an already-billed order onto a customer's khata — a new customer, or one picked in
    /// the dialog. The order itself is untouched (it is a finished sale, already counted in the
    /// figures above); this only records the udhaar so it shows up in Len-Den under that customer.
    /// </summary>
    public void AddOrderToLedger(ReportRow row, long? existingCustomerId, string name, string mobile)
    {
        name = (name ?? "").Trim();
        mobile = (mobile ?? "").Trim();

        var customerId = existingCustomerId ?? _ledgerRepo.SaveCustomer(new Customer
        {
            ClientId = _client.ClientId,
            Name = string.IsNullOrWhiteSpace(name) ? "Customer" : name,
            Phone = mobile,
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        });

        _ledgerRepo.AddLedgerEntry(new LedgerEntry
        {
            ClientId = _client.ClientId,
            CustomerId = customerId,
            Type = "gave",              // debit — the customer owes this
            Amount = row.Order.TotalAmount,
            PaymentMode = "credit",
            Remarks = $"Bill {row.BillNoText}",
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        });
    }

    /// <summary>
    /// Rebuilds the counter list and points the filter back at the business now signed in.
    ///
    /// Needed because this view model is a singleton that outlives a shift: without it, the
    /// manager who takes over after a logout would open Reports still filtered to the previous
    /// brand, reading its takings as their own.
    /// </summary>
    public void SyncToSession()
    {
        _suspendReload = true;
        LoadCounters();
        _suspendReload = false;
        Load();
    }

    private void LoadCounters()
    {
        // The client the till is billing for right now. ClientContext, not Session: it is set at
        // login and defaults sensibly on a till whose staff list has never synced (where nobody
        // signs in), so the filter still opens on a real business rather than blank.
        var current = _client.ClientId;

        // Names can change — a rename lands here before the filter is next rebuilt — so drop the
        // cached prefixes with the list they were keyed to.
        _prefixByClient.Clear();
        Counters.Clear();
        Counters.Add(AllCounters);
        foreach (var c in _repo.GetCounters())
        {
            Counters.Add(c);
        }

        // Default to the signed-in business's own day — "what did this counter take today" is the
        // question the page is opened to answer. The other brand, and the combined total, are one
        // dropdown away.
        SelectedCounter = Counters.FirstOrDefault(c => c.Id == current) ?? Counters[0];
    }

    partial void OnSelectedCounterChanged(ReportCounter? value)
    {
        if (!_suspendReload)
        {
            Load();
        }
    }

    private static string IstToday(int offsetDays = 0)
        => DateTime.UtcNow.AddMinutes(330).AddDays(-offsetDays).ToString("yyyy-MM-dd");

    [RelayCommand]
    private void SetRange(string type)
    {
        RangeType = type;
        IsCustomRange = type == "custom";
        switch (type)
        {
            case "today": StartDate = IstToday(0); EndDate = IstToday(0); break;
            case "yesterday": StartDate = IstToday(1); EndDate = IstToday(1); break;
            case "week": StartDate = IstToday(6); EndDate = IstToday(0); break;
            case "month": StartDate = IstToday(29); EndDate = IstToday(0); break;
            case "custom": return; // wait for ApplyCustom
        }
        OnPropertyChanged(nameof(PeriodDisplay));
        Load();
    }

    [RelayCommand]
    private void ApplyCustom()
    {
        RangeType = "custom";
        StartDate = CustomStart.ToString("yyyy-MM-dd");
        EndDate = (CustomEnd < CustomStart ? CustomStart : CustomEnd).ToString("yyyy-MM-dd");
        OnPropertyChanged(nameof(PeriodDisplay));
        Load();
    }

    [RelayCommand] private void NextPage() { if (CurrentPage < TotalPages - 1) { CurrentPage++; RenderPage(); } }
    [RelayCommand] private void PrevPage() { if (CurrentPage > 0) { CurrentPage--; RenderPage(); } }

    public IReadOnlyList<OrderItem> LoadItems(long orderId)
    {
        var row = System.Linq.Enumerable.FirstOrDefault(_all, r => r.Order.Id == orderId);
        if (row?.Order.Items != null && row.Order.Items.Count > 0)
        {
            return row.Order.Items;
        }
        return _repo.GetItems(orderId);
    }

    /// <summary>Re-reads the current range and staff selection from the database.</summary>
    public void Reload() => Load();

    private static string? GetJsonString(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null && p.ValueKind != JsonValueKind.Undefined)
        {
            return p.ToString();
        }
        return null;
    }

    private static double GetJsonDouble(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null && p.ValueKind != JsonValueKind.Undefined)
        {
            if (double.TryParse(p.ToString(), out var d))
            {
                return d;
            }
        }
        return 0.0;
    }

    private static long? GetJsonLong(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null && p.ValueKind != JsonValueKind.Undefined)
        {
            if (long.TryParse(p.ToString(), out var l))
            {
                return l;
            }
        }
        return null;
    }

    private Order? ParseOrder(JsonElement el)
    {
        try
        {
            var o = new Order();
            o.Id = GetJsonLong(el, "id") ?? 0;
            o.Uuid = GetJsonString(el, "sqlite_uuid") ?? GetJsonString(el, "uuid");
            o.ClientId = GetJsonLong(el, "client_id") ?? 0;
            o.TableId = GetJsonLong(el, "table_id");
            o.OrderStatus = GetJsonString(el, "order_status") ?? "settled";
            o.TotalAmount = GetJsonDouble(el, "total_amount");
            o.DiscountAmount = GetJsonDouble(el, "discount_amount");
            o.CustomerName = GetJsonString(el, "customer_name");
            o.CustomerMobile = GetJsonString(el, "customer_mobile");
            o.BilledAt = GetJsonString(el, "billed_at");
            o.BillNumber = GetJsonLong(el, "bill_number");
            o.TableNumber = GetJsonString(el, "table_number");
            o.CreatedByName = GetJsonString(el, "created_by_name");

            if (el.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var itemEl in itemsProp.EnumerateArray())
                {
                    var item = new OrderItem();
                    item.Id = GetJsonLong(itemEl, "id") ?? 0;
                    item.OrderId = GetJsonLong(itemEl, "order_id") ?? 0;
                    item.ItemId = GetJsonLong(itemEl, "item_id");
                    item.ItemName = GetJsonString(itemEl, "item_name");
                    item.Price = GetJsonDouble(itemEl, "price");
                    item.Quantity = GetJsonLong(itemEl, "quantity") ?? 0;
                    item.Total = GetJsonDouble(itemEl, "total");
                    item.DiscountAmount = GetJsonDouble(itemEl, "discount_amount");
                    item.DiscountType = GetJsonString(itemEl, "discount_type");
                    item.DiscountValue = GetJsonDouble(itemEl, "discount_value");
                    item.DiscountLabel = GetJsonString(itemEl, "discount_label");
                    o.Items.Add(item);
                }
            }

            return o;
        }
        catch
        {
            return null;
        }
    }

    private async void Load()
    {
        var clientId = SelectedCounter?.Id;
        var clientSlug = SelectedCounter?.Slug ?? _client.Slug;
        if (string.IsNullOrWhiteSpace(clientSlug))
        {
            clientSlug = _client.Slug;
        }

        bool loadedFromServer = false;
        try
        {
            var api = new PosApiClient(_sync.ApiUrl, clientSlug, clientId ?? 0, TimeSpan.FromSeconds(5));
            var query = $"/reports/summary?start_date={StartDate}&end_date={EndDate}&report_client={clientSlug}";
            var root = await api.GetAsync(query);
            if (root is { } jsonDoc)
            {
                var data = jsonDoc.TryGetProperty("data", out var d) ? d : jsonDoc;
                
                // Parse KPIs / Summary
                if (data.TryGetProperty("today", out var todayProp))
                {
                    var totalSales = todayProp.TryGetProperty("revenue", out var revProp) ? double.Parse(revProp.ToString()) : 0.0;
                    var totalOrders = todayProp.TryGetProperty("count", out var cntProp) ? int.Parse(cntProp.ToString()) : 0;
                    
                    TotalRevenue = "₹" + totalSales.ToString("N0", CultureInfo.InvariantCulture);
                    TotalBills = totalOrders.ToString();
                    TotalDiscounts = "₹0";
                }
                else if (data.TryGetProperty("kpis", out var kpiProp))
                {
                    var totalSales = kpiProp.TryGetProperty("today_sale", out var revProp) ? double.Parse(revProp.ToString()) : 0.0;
                    TotalRevenue = "₹" + totalSales.ToString("N0", CultureInfo.InvariantCulture);
                    TotalBills = "0";
                    TotalDiscounts = "₹0";
                }

                // Parse Range Orders / Settled Orders
                _all.Clear();
                if (data.TryGetProperty("range_orders", out var rangeOrdersProp) && rangeOrdersProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var oEl in rangeOrdersProp.EnumerateArray())
                    {
                        var order = ParseOrder(oEl);
                        if (order != null)
                        {
                            _all.Add(new ReportRow(order, PrefixFor(order.ClientId)));
                        }
                    }
                    TotalBills = _all.Count.ToString();
                }
                else if (data.TryGetProperty("recent_bills", out var recentBillsProp) && recentBillsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var oEl in recentBillsProp.EnumerateArray())
                    {
                        var order = ParseOrder(oEl);
                        if (order != null)
                        {
                            _all.Add(new ReportRow(order, PrefixFor(order.ClientId)));
                        }
                    }
                    TotalBills = _all.Count.ToString();
                }

                // Category-wise tab: the server already rolls up every sold item with its category
                // for the same range ("all_sold_items"), so the breakdown matches the log's totals.
                PopulateCategorySales(ParseServerSoldItems(data));
                loadedFromServer = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[REPORT SYNC ERROR] Failed to fetch live reports: {ex.Message}");
        }

        if (!loadedFromServer)
        {
            var s = _repo.GetSummary(StartDate, EndDate, clientId: clientId);
            TotalRevenue = "₹" + s.TotalSales.ToString("N0", CultureInfo.InvariantCulture);
            TotalBills = s.TotalOrders.ToString();
            TotalDiscounts = "₹" + s.TotalDiscounts.ToString("N0", CultureInfo.InvariantCulture);

            _all.Clear();
            foreach (var o in _repo.GetSettledOrders(StartDate, EndDate, clientId: clientId))
            {
                _all.Add(new ReportRow(o, PrefixFor(o.ClientId)));
            }

            // Offline / local: read the breakdown from the till's own stored line items.
            PopulateCategorySales(_repo.GetCategoryItemSales(StartDate, EndDate, clientId: clientId));
        }

        ApplyFilter();
    }

    /// <summary>
    /// The server's full "every item sold in the range" rollup, tagged with each item's category —
    /// the data behind the category-wise tab when the report came from the server. One entry per
    /// item, already summed, so it lines up with the log's revenue.
    /// </summary>
    private static IReadOnlyList<CategoryItemSale> ParseServerSoldItems(JsonElement data)
    {
        var list = new List<CategoryItemSale>();
        if (!data.TryGetProperty("all_sold_items", out var sold) || sold.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var it in sold.EnumerateArray())
        {
            if (it.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            var qty = JsonLong(it, "qty");
            list.Add(new CategoryItemSale
            {
                CategoryName = JsonStr(it, "category") is { Length: > 0 } c ? c : "Other",
                SubCategoryName = JsonStr(it, "sub_category"),
                ItemName = JsonStr(it, "name") is { Length: > 0 } n ? n : "Item",
                Qty = qty > 0 ? qty : 1,
                Amount = JsonDouble(it, "amount"),
            });
        }
        return list;
    }

    private static string JsonStr(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString())
            : "";

    private static long JsonLong(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.TryGetInt64(out var n) ? n : (long)v.GetDouble();
        return long.TryParse(JsonStr(e, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0;
    }

    private static double JsonDouble(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        return double.TryParse(JsonStr(e, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0;
    }

    /// <summary>While true the filter setters don't rebuild the list — used to load raw data and
    /// reset the dropdowns in one go, then rebuild once at the end.</summary>
    private bool _suspendCategoryRebuild;

    /// <summary>
    /// Takes a fresh period's item rollup, refreshes the category tab's dropdown options (keeping
    /// the user's current pick if it still exists), then draws the filtered list.
    /// </summary>
    private void PopulateCategorySales(IReadOnlyList<CategoryItemSale> raw)
    {
        _categoryRaw = raw;
        _suspendCategoryRebuild = true;

        // Category options: "All" plus each category present, biggest earner first.
        var cats = raw
            .GroupBy(x => CatKey(x))
            .OrderByDescending(g => g.Sum(y => y.Amount))
            .Select(g => g.Key)
            .ToList();
        CategoryFilterOptions.Clear();
        CategoryFilterOptions.Add(AllFilter);
        foreach (var c in cats)
        {
            CategoryFilterOptions.Add(c);
        }
        if (!CategoryFilterOptions.Contains(SelectedCategoryFilter))
        {
            SelectedCategoryFilter = AllFilter;
        }

        RebuildSubCategoryOptions();

        _suspendCategoryRebuild = false;
        RebuildCategoryGroups();
    }

    /// <summary>Sub-category dropdown for the currently picked category ("All" while no specific
    /// category is chosen, since sub-categories only make sense within one).</summary>
    private void RebuildSubCategoryOptions()
    {
        SubCategoryFilterOptions.Clear();
        SubCategoryFilterOptions.Add(AllFilter);

        if (SelectedCategoryFilter != AllFilter)
        {
            var subs = _categoryRaw
                .Where(x => CatKey(x) == SelectedCategoryFilter && !string.IsNullOrWhiteSpace(x.SubCategoryName))
                .Select(x => x.SubCategoryName)
                .Distinct()
                .OrderBy(s => s);
            foreach (var s in subs)
            {
                SubCategoryFilterOptions.Add(s);
            }
        }

        if (!SubCategoryFilterOptions.Contains(SelectedSubCategoryFilter))
        {
            SelectedSubCategoryFilter = AllFilter;
        }
    }

    /// <summary>
    /// Draws the category tab from <see cref="_categoryRaw"/> under the current filters: category,
    /// sub-category and a name search. With no category picked the groups are the categories; pick
    /// one and it drills in, the groups becoming that category's sub-categories.
    /// </summary>
    private void RebuildCategoryGroups()
    {
        if (_suspendCategoryRebuild)
        {
            return;
        }

        var q = (CategorySearch ?? "").Trim().ToLowerInvariant();
        var allCat = SelectedCategoryFilter is null or AllFilter;
        var allSub = SelectedSubCategoryFilter is null or AllFilter;

        var filtered = _categoryRaw.Where(x =>
            (allCat || CatKey(x) == SelectedCategoryFilter)
            && (allSub || x.SubCategoryName == SelectedSubCategoryFilter)
            && (q.Length == 0 || x.ItemName.ToLowerInvariant().Contains(q)));

        // Group by category normally; once inside a category, group by its sub-categories.
        Func<CategoryItemSale, string> key = allCat
            ? CatKey
            : x => string.IsNullOrWhiteSpace(x.SubCategoryName) ? "(No sub-category)" : x.SubCategoryName;

        var groups = filtered
            .GroupBy(key)
            .Select(g => new CategorySalesGroup(
                g.Key,
                g.Sum(x => x.Qty),
                g.Sum(x => x.Amount),
                g.GroupBy(x => x.ItemName)
                    .Select(itemG => new CategoryItemRow(itemG.Key, itemG.Sum(y => y.Qty), itemG.Sum(y => y.Amount)))
                    .OrderByDescending(i => i.Qty)
                    .ToList()))
            .OrderByDescending(g => g.TotalAmount)
            .ToList();

        CategorySales.Clear();
        foreach (var g in groups)
        {
            CategorySales.Add(g);
        }
        OnPropertyChanged(nameof(CategoryCountText));
        OnPropertyChanged(nameof(HasCategorySales));
        OnPropertyChanged(nameof(HasNoCategorySales));
    }

    /// <summary>An item's category name, with the blank/unknown case folded to "Other" so it never
    /// splits into two groups.</summary>
    private static string CatKey(CategoryItemSale x)
        => string.IsNullOrWhiteSpace(x.CategoryName) ? "Other" : x.CategoryName;

    /// <summary>This client's bill prefix, resolved once and cached — one lookup per business
    /// even when "All" mixes several into the same page.</summary>
    private string PrefixFor(long clientId)
    {
        if (!_prefixByClient.TryGetValue(clientId, out var prefix))
        {
            prefix = _repo.BillPrefix(clientId);
            _prefixByClient[clientId] = prefix;
        }
        return prefix;
    }

    partial void OnSearchTermChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var q = SearchTerm?.Trim().ToLowerInvariant() ?? "";
        _filtered = _all.Where(r =>
            q.Length == 0
            || r.BillNoText.ToLowerInvariant().Contains(q)
            || r.TableText.ToLowerInvariant().Contains(q)
            || r.TotalText.Contains(q)
            || r.StaffText.ToLowerInvariant().Contains(q)
            || (r.Order.CustomerName ?? "").ToLowerInvariant().Contains(q)).ToList();
        CurrentPage = 0;
        RenderPage();
        OnPropertyChanged(nameof(OrderCount));
        OnPropertyChanged(nameof(TotalPages));
    }

    private void RenderPage()
    {
        Orders.Clear();
        foreach (var r in _filtered.Skip(CurrentPage * PageSize).Take(PageSize))
        {
            Orders.Add(r);
        }
        OnPropertyChanged(nameof(PageInfo));
    }

    [RelayCommand]
    private void ExportCsv()
    {
        try
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            // The counter goes in the filename too, so exporting Daal Roti's day and then Chay
            // Chaupal's for the same dates doesn't overwrite the first file.
            var who = SelectedCounter?.Id is null ? "all" : Slug(SelectedCounter.Name);
            var path = Path.Combine(docs, $"sales_report_{who}_{StartDate}_to_{EndDate}.csv");
            var sb = new StringBuilder();
            sb.AppendLine("Bill Number,Date,Staff,Customer,Mobile,Table,Discount,Total,Note");
            foreach (var r in _filtered)
            {
                var o = r.Order;
                sb.AppendLine(string.Join(",",
                    Csv(r.BillNoText), Csv(r.DateText), Csv(r.StaffText),
                    Csv(o.CustomerName ?? ""), Csv(o.CustomerMobile ?? ""),
                    Csv(r.TableText), o.DiscountAmount.ToString("0.##"), o.TotalAmount.ToString("0.##"), Csv(o.BillNote ?? "")));
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            ExportMessage = $"Exported ✓  {path}";
        }
        catch (Exception ex)
        {
            ExportMessage = "Export failed: " + ex.Message;
        }
    }

    private static string Csv(string v) => v.Contains(',') ? $"\"{v.Replace("\"", "\"\"")}\"" : v;

    /// <summary>Counter name reduced to something safe to put in a filename.</summary>
    private static string Slug(string name)
    {
        var cleaned = new string(name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_').ToArray());
        return cleaned.Trim('_') is { Length: > 0 } s ? s : "counter";
    }

    private static string Fmt(string ymd)
        => DateTime.TryParse(ymd, out var d) ? d.ToString("dd MMM yyyy") : ymd;
}

/// <summary>One row in the Order Log table (formatted for display).</summary>
public sealed class ReportRow
{
    public Order Order { get; }
    public string BillNoText { get; }
    public string DateText { get; }
    public string TableText { get; }
    public string TotalText { get; }
    public string StaffText { get; }

    public ReportRow(Order o, string prefix)
    {
        Order = o;
        // Bills written before sign-in existed carry no operator; they are still real sales,
        // so they show a dash rather than being hidden or blamed on someone.
        StaffText = string.IsNullOrWhiteSpace(o.CreatedByName) ? "—" : o.CreatedByName!;
        BillNoText = o.BillNumber.HasValue
            ? BillPrefix.Format(prefix, o.BillNumber.Value)
            : $"#{o.Id}";
        DateText = DateTime.TryParse(o.BilledAt ?? o.CreatedAt, out var d)
            ? d.ToString("dd/MM/yyyy HH:mm")
            : (o.BilledAt ?? "");
        TableText = string.IsNullOrWhiteSpace(o.TableNumber) ? "—" : o.TableNumber!;
        TotalText = "₹" + o.TotalAmount.ToString("0.##", CultureInfo.InvariantCulture);
    }
}

/// <summary>One category on the category-wise tab: its total for the period and the items under
/// it, biggest sellers first.</summary>
public sealed class CategorySalesGroup
{
    public CategorySalesGroup(string name, long totalQty, double totalAmount, IReadOnlyList<CategoryItemRow> items)
    {
        CategoryName = name;
        TotalQty = totalQty;
        TotalAmount = totalAmount;
        Items = items;
    }

    public string CategoryName { get; }
    public long TotalQty { get; }
    public double TotalAmount { get; }
    public IReadOnlyList<CategoryItemRow> Items { get; }

    public string QtyText => $"{TotalQty} qty";
    public string AmountText => "₹" + TotalAmount.ToString("N0", CultureInfo.InvariantCulture);
}

/// <summary>One item line inside a category group.</summary>
public sealed class CategoryItemRow
{
    public CategoryItemRow(string name, long qty, double amount)
    {
        Name = name;
        Qty = qty;
        Amount = amount;
    }

    public string Name { get; }
    public long Qty { get; }
    public double Amount { get; }

    public string QtyText => $"{Qty}";
    public string AmountText => "₹" + Amount.ToString("N0", CultureInfo.InvariantCulture);
}
