using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.App.Services;
using Pos.Core.Models;
using Pos.Core.Repositories;

namespace Pos.App.ViewModels;

public partial class ReportsViewModel : ObservableObject
{
    private const int PageSize = 12;
    private static readonly ReportStaff AllStaff = new() { Id = null, Name = "Sabhi Staff" };

    private readonly ReportsRepository _repo;
    private readonly List<ReportRow> _all = new();       // full result for the range
    private List<ReportRow> _filtered = new();           // after search
    private string _prefix = "DR";
    private bool _suspendReload;                         // while the staff list is rebuilt

    public ObservableCollection<ReportRow> Orders { get; } = new();   // current page

    /// <summary>"All staff" plus each active operator, for the staff filter.</summary>
    public ObservableCollection<ReportStaff> Staff { get; } = new();

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
    [ObservableProperty] private ReportStaff? _selectedStaff;

    public string PeriodDisplay => $"{Fmt(StartDate)}   to   {Fmt(EndDate)}";
    public int OrderCount => _filtered.Count;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(_filtered.Count / (double)PageSize));
    public string PageInfo => $"Page {CurrentPage + 1} of {TotalPages}";

    public ReportsViewModel(ReportsRepository repo)
    {
        _repo = repo;
        _suspendReload = true;
        LoadStaff();
        _suspendReload = false;
        SetRange("today");
    }

    /// <summary>
    /// Rebuilds the staff list and points the filter back at whoever is signed in.
    ///
    /// Needed because this view model is a singleton that outlives a shift: without it, the
    /// operator who takes over after a logout would open Reports to the previous person's
    /// sales, which reads as the shop having taken their money.
    /// </summary>
    public void SyncToSession()
    {
        _suspendReload = true;
        LoadStaff();
        _suspendReload = false;
        Load();
    }

    private void LoadStaff()
    {
        var current = Session.User?.Id;

        Staff.Clear();
        Staff.Add(AllStaff);
        foreach (var s in _repo.GetStaff())
        {
            Staff.Add(s);
        }

        // Default to the operator's own bills — "what did I take today" is the question this
        // page gets opened to answer. The whole shop is one dropdown away.
        SelectedStaff = Staff.FirstOrDefault(s => s.Id == current) ?? Staff[0];
    }

    partial void OnSelectedStaffChanged(ReportStaff? value)
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

    public IReadOnlyList<OrderItem> LoadItems(long orderId) => _repo.GetItems(orderId);

    /// <summary>Re-reads the current range and staff selection from the database.</summary>
    public void Reload() => Load();

    private void Load()
    {
        var userId = SelectedStaff?.Id;

        _prefix = _repo.BillPrefix();
        var s = _repo.GetSummary(StartDate, EndDate, userId: userId);
        TotalRevenue = "₹" + s.TotalSales.ToString("N0", CultureInfo.InvariantCulture);
        TotalBills = s.TotalOrders.ToString();
        TotalDiscounts = "₹" + s.TotalDiscounts.ToString("N0", CultureInfo.InvariantCulture);

        _all.Clear();
        foreach (var o in _repo.GetSettledOrders(StartDate, EndDate, userId: userId))
        {
            _all.Add(new ReportRow(o, _prefix));
        }
        ApplyFilter();
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
            // The staff goes in the filename too, so two operators exporting the same day
            // don't overwrite each other's file.
            var who = SelectedStaff?.Id is null ? "all" : Slug(SelectedStaff.Name);
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

    /// <summary>Staff name reduced to something safe to put in a filename.</summary>
    private static string Slug(string name)
    {
        var cleaned = new string(name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_').ToArray());
        return cleaned.Trim('_') is { Length: > 0 } s ? s : "staff";
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
            ? $"#{prefix}-{o.BillNumber.Value.ToString().PadLeft(4, '0')}"
            : $"#{o.Id}";
        DateText = DateTime.TryParse(o.BilledAt ?? o.CreatedAt, out var d)
            ? d.ToString("dd/MM/yyyy HH:mm")
            : (o.BilledAt ?? "");
        TableText = string.IsNullOrWhiteSpace(o.TableNumber) ? "—" : o.TableNumber!;
        TotalText = "₹" + o.TotalAmount.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
