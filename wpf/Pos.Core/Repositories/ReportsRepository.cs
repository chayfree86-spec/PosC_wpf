using Dapper;
using Pos.Core.Data;
using Pos.Core.Models;

namespace Pos.Core.Repositories;

/// <summary>
/// Read-only queries for the Sales Reports page. Mirrors the Electron app's
/// getReportsSummary + getOrders(status: settled,completed) over a date range.
/// Only report-visible, settled/completed orders count as sales.
/// </summary>
public sealed class ReportsRepository
{
    private readonly DatabaseService _db;

    public ReportsRepository(DatabaseService db)
    {
        _db = db;
        DapperConfig.Init();
    }

    public ReportSummary GetSummary(string startDate, string endDate, long clientId = 1)
    {
        using var conn = _db.OpenConnection();
        return conn.QueryFirst<ReportSummary>(
            @"SELECT COALESCE(SUM(total_amount), 0)     AS total_sales,
                     COUNT(*)                            AS total_orders,
                     COALESCE(SUM(discount_amount), 0)   AS total_discounts
              FROM orders
              WHERE client_id = @clientId
                AND report_visible = 1
                AND order_status IN ('settled', 'completed')
                AND date(billed_at) >= @startDate
                AND date(billed_at) <= @endDate",
            new { clientId, startDate, endDate });
    }

    public IReadOnlyList<Order> GetSettledOrders(string startDate, string endDate, long clientId = 1)
    {
        using var conn = _db.OpenConnection();
        return conn.Query<Order>(
            @"SELECT o.*, rt.table_number
              FROM orders o
              LEFT JOIN restaurant_tables rt ON rt.id = o.table_id
              WHERE o.client_id = @clientId
                AND o.report_visible = 1
                AND o.order_status IN ('settled', 'completed')
                AND date(o.billed_at) >= @startDate
                AND date(o.billed_at) <= @endDate
              ORDER BY o.billed_at DESC, o.id DESC",
            new { clientId, startDate, endDate }).AsList();
    }

    /// <summary>Line items for a single order (for the bill view).</summary>
    public IReadOnlyList<OrderItem> GetItems(long orderId)
    {
        using var conn = _db.OpenConnection();
        return conn.Query<OrderItem>(
            "SELECT * FROM order_items WHERE order_id = @orderId ORDER BY id", new { orderId }).AsList();
    }

    /// <summary>Client bill-number prefix (CC / DR / derived), matching NextBillNumber.</summary>
    public string BillPrefix(long clientId = 1)
    {
        using var conn = _db.OpenConnection();
        var slug = (conn.QueryFirstOrDefault<string>(
            "SELECT slug FROM clients WHERE id = @clientId", new { clientId }) ?? "").ToLowerInvariant();
        if (slug.Contains("chaychaupal") || slug.Contains("chay") || slug.Contains("cc")) return "CC";
        return "DR";
    }
}
