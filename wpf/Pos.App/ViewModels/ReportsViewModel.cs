using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Models;
using Pos.Core.Repositories;

namespace Pos.App.ViewModels;

public partial class ReportsViewModel : ObservableObject
{
    private const int PageSize = 12;
    private readonly ReportsRepository _repo;
    private readonly List<ReportRow> _all = new();       // full result for the range
    private List<ReportRow> _filtered = new();           // after search
    private string _prefix = "DR";

    public ObservableCollection<ReportRow> Orders { get; } = new();   // current page

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

    public string PeriodDisplay => $"{Fmt(StartDate)}   to   {Fmt(EndDate)}";
    public int OrderCount => _filtered.Count;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(_filtered.Count / (double)PageSize));
    public string PageInfo => $"Page {CurrentPage + 1} of {TotalPages}";

    public ReportsViewModel(ReportsRepository repo)
    {
        _repo = repo;
        SetRange("today");
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

    private void Load()
    {
        _prefix = _repo.BillPrefix();
        var s = _repo.GetSummary(StartDate, EndDate);
        TotalRevenue = "₹" + s.TotalSales.ToString("N0", CultureInfo.InvariantCulture);
        TotalBills = s.TotalOrders.ToString();
        TotalDiscounts = "₹" + s.TotalDiscounts.ToString("N0", CultureInfo.InvariantCulture);

        _all.Clear();
        foreach (var o in _repo.GetSettledOrders(StartDate, EndDate))
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
            var path = Path.Combine(docs, $"sales_report_{StartDate}_to_{EndDate}.csv");
            var sb = new StringBuilder();
            sb.AppendLine("Bill Number,Date,Customer,Mobile,Table,Discount,Total,Note");
            foreach (var r in _filtered)
            {
                var o = r.Order;
                sb.AppendLine(string.Join(",",
                    Csv(r.BillNoText), Csv(r.DateText), Csv(o.CustomerName ?? ""), Csv(o.CustomerMobile ?? ""),
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

    public ReportRow(Order o, string prefix)
    {
        Order = o;
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
