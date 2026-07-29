namespace Pos.Core.Models;

/// <summary>Aggregated sales figures for the Reports page (a date range).</summary>
public sealed class ReportSummary
{
    public double TotalSales { get; set; }
    public long TotalOrders { get; set; }
    public double TotalDiscounts { get; set; }
}

/// <summary>One entry in the Reports page's staff filter.</summary>
public sealed class ReportStaff
{
    /// <summary>Null on the "All staff" entry, which is what the queries take to mean
    /// "don't filter".</summary>
    public long? Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>What a ComboBox row reports to automation and screen readers, which read the
    /// item itself rather than the template that draws it — without this they announce the
    /// type name.</summary>
    public override string ToString() => Name;
}
