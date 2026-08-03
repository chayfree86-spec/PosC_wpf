namespace Pos.Core.Models;

/// <summary>Aggregated sales figures for the Reports page (a date range).</summary>
public sealed class ReportSummary
{
    public double TotalSales { get; set; }
    public long TotalOrders { get; set; }
    public double TotalDiscounts { get; set; }
}

/// <summary>A best-seller for the Orders screen's "Most Selling Items" panel: an item and how
/// many of it have been sold in total.</summary>
public sealed class PopularItem
{
    public long ItemId { get; set; }
    public string Name { get; set; } = "";
    public long Qty { get; set; }

    /// <summary>The badge on the right of each row, e.g. "42 sold".</summary>
    public string QtyText => $"{Qty} sold";
}

/// <summary>
/// One (category, item) line in the report's category-wise breakdown: how much of an item sold in
/// the period and what it took, together with the category it belongs to for grouping.
/// </summary>
public sealed class CategoryItemSale
{
    public long CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
    public string SubCategoryName { get; set; } = "";
    public string ItemName { get; set; } = "";
    public long Qty { get; set; }
    public double Amount { get; set; }
}

/// <summary>
/// One entry in the Reports page's counter filter — a business billing on this till.
///
/// The page used to filter by operator; on a shared counter the more useful question is which
/// BUSINESS took the money, since Daal Roti and Chay Chaupal sell from the same machine. The
/// operator who rang each bill still shows in the log's own column.
/// </summary>
public sealed class ReportCounter
{
    /// <summary>Null on the "All" entry, which is what the queries take to mean "every business
    /// on this till, combined".</summary>
    public long? Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";

    /// <summary>What a ComboBox row reports to automation and screen readers, which read the
    /// item itself rather than the template that draws it — without this they announce the
    /// type name.</summary>
    public override string ToString() => Name;
}
