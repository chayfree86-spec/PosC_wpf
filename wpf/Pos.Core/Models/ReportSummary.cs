namespace Pos.Core.Models;

/// <summary>Aggregated sales figures for the Reports page (a date range).</summary>
public sealed class ReportSummary
{
    public double TotalSales { get; set; }
    public long TotalOrders { get; set; }
    public double TotalDiscounts { get; set; }
}
