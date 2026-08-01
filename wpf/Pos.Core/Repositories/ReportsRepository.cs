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

    /// <param name="clientId">
    /// Which business to add up. Null totals every business on the till — "what did this counter
    /// take today" across both brands — so the caller has to say which one it means; there is no
    /// implicit default to whoever is signed in, because the page lets the manager look at the
    /// other brand's day too.
    /// </param>
    public ReportSummary GetSummary(string startDate, string endDate, long? clientId = null)
    {
        using var conn = _db.OpenConnection();
        return conn.QueryFirst<ReportSummary>(
            @"SELECT COALESCE(SUM(total_amount), 0)     AS total_sales,
                     COUNT(*)                            AS total_orders,
                     COALESCE(SUM(discount_amount), 0)   AS total_discounts
              FROM orders
              WHERE report_visible = 1
                AND order_status IN ('settled', 'completed')
                AND date(billed_at) >= @startDate
                AND date(billed_at) <= @endDate
                AND (@clientId IS NULL OR client_id = @clientId)",
            new { clientId, startDate, endDate });
    }

    public IReadOnlyList<Order> GetSettledOrders(string startDate, string endDate, long? clientId = null)
    {
        using var conn = _db.OpenConnection();
        return conn.Query<Order>(
            @"SELECT o.*, rt.table_number, u.name AS created_by_name
              FROM orders o
              LEFT JOIN restaurant_tables rt ON rt.id = o.table_id
              LEFT JOIN users u ON u.id = o.created_by
              WHERE o.report_visible = 1
                AND o.order_status IN ('settled', 'completed')
                AND date(o.billed_at) >= @startDate
                AND date(o.billed_at) <= @endDate
                AND (@clientId IS NULL OR o.client_id = @clientId)
              ORDER BY o.billed_at DESC, o.id DESC",
            new { clientId, startDate, endDate }).AsList();
    }

    /// <summary>
    /// The businesses the filter can pick from — every client the till knows about, so a manager
    /// can read either brand's day. Ordered by id, which keeps the two founding brands stable at
    /// the top of the list as others are added.
    /// </summary>
    public IReadOnlyList<ReportCounter> GetCounters()
    {
        using var conn = _db.OpenConnection();
        return conn.Query<ReportCounter>(
            "SELECT id, name FROM clients ORDER BY id").AsList();
    }

    /// <summary>Line items for a single order (for the bill view).</summary>
    public IReadOnlyList<OrderItem> GetItems(long orderId)
    {
        using var conn = _db.OpenConnection();
        return conn.Query<OrderItem>(
            "SELECT * FROM order_items WHERE order_id = @orderId ORDER BY id", new { orderId }).AsList();
    }

    /// <summary>
    /// The letters in front of every bill number on this page — abbreviated from the business
    /// name, the same value <c>OrderRepository.NextBillNumber</c> stamps on the bill itself.
    ///
    /// Both used to work the prefix out separately and disagreed: the till derived initials for
    /// an unrecognised business while this returned "DR", so a third shop's sales were listed
    /// under Daal Roti's prefix even though its bills printed its own. One column answers both
    /// now — qualified because this method shares its name with the helper.
    /// </summary>
    public string BillPrefix(long? clientId = null)
    {
        clientId ??= _client.ClientId;
        using var conn = _db.OpenConnection();
        return Data.BillPrefix.Resolve(conn, null, clientId.Value);
    }
}
