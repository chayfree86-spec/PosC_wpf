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


    /// <summary>Which business the till is billing for; every client-scoped read and write
    /// below defaults to it so no call site has to remember to pass one.</summary>
    private readonly ClientContext _client;

    public ReportsRepository(DatabaseService db, ClientContext client)
    {
        _db = db;
        _client = client;
        DapperConfig.Init();
    }

    /// <param name="userId">
    /// Restricts the figures to one operator's bills. Null means the whole shop — the two are
    /// different questions ("what did I take today" vs "what did the counter take"), so the
    /// caller has to say which one it is asking.
    /// </param>
    public ReportSummary GetSummary(string startDate, string endDate, long? clientId = null, long? userId = null)
    {
        clientId ??= _client.ClientId;
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
                AND date(billed_at) <= @endDate
                AND (@userId IS NULL OR created_by = @userId)",
            new { clientId, startDate, endDate, userId });
    }

    public IReadOnlyList<Order> GetSettledOrders(string startDate, string endDate, long? clientId = null, long? userId = null)
    {
        clientId ??= _client.ClientId;
        using var conn = _db.OpenConnection();
        return conn.Query<Order>(
            @"SELECT o.*, rt.table_number, u.name AS created_by_name
              FROM orders o
              LEFT JOIN restaurant_tables rt ON rt.id = o.table_id
              LEFT JOIN users u ON u.id = o.created_by
              WHERE o.client_id = @clientId
                AND o.report_visible = 1
                AND o.order_status IN ('settled', 'completed')
                AND date(o.billed_at) >= @startDate
                AND date(o.billed_at) <= @endDate
                AND (@userId IS NULL OR o.created_by = @userId)
              ORDER BY o.billed_at DESC, o.id DESC",
            new { clientId, startDate, endDate, userId }).AsList();
    }

    /// <summary>The staff the filter can pick from — the shop's active operators.</summary>
    public IReadOnlyList<ReportStaff> GetStaff(long? clientId = null)
    {
        clientId ??= _client.ClientId;
        using var conn = _db.OpenConnection();
        return conn.Query<ReportStaff>(
            @"SELECT id, name
              FROM users
              WHERE client_id = @clientId AND is_active = 1
              ORDER BY name",
            new { clientId }).AsList();
    }

    /// <summary>Line items for a single order (for the bill view).</summary>
    public IReadOnlyList<OrderItem> GetItems(long orderId)
    {
        using var conn = _db.OpenConnection();
        return conn.Query<OrderItem>(
            "SELECT * FROM order_items WHERE order_id = @orderId ORDER BY id", new { orderId }).AsList();
    }

    /// <summary>Client bill-number prefix (CC / DR / derived), matching NextBillNumber.</summary>
    public string BillPrefix(long? clientId = null)
    {
        clientId ??= _client.ClientId;
        using var conn = _db.OpenConnection();
        var slug = (conn.QueryFirstOrDefault<string>(
            "SELECT slug FROM clients WHERE id = @clientId", new { clientId }) ?? "").ToLowerInvariant();
        if (slug.Contains("chaychaupal") || slug.Contains("chay") || slug.Contains("cc")) return "CC";
        return "DR";
    }
}
