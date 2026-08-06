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
            @"SELECT COALESCE(SUM(total_amount), 0.0)     AS total_sales,
                     COUNT(*)                            AS total_orders,
                     COALESCE(SUM(discount_amount), 0.0)   AS total_discounts
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
            "SELECT id, name, slug FROM clients ORDER BY id").AsList();
    }

    /// <summary>Line items for a single order (for the bill view).</summary>
    public IReadOnlyList<OrderItem> GetItems(long orderId)
    {
        using var conn = _db.OpenConnection();
        return conn.Query<OrderItem>(
            "SELECT * FROM order_items WHERE order_id = @orderId ORDER BY id", new { orderId }).AsList();
    }

    /// <summary>
    /// Every item sold in the period, with its category — the data behind the report's
    /// category-wise breakdown. One row per (category, item): quantity sold and rupees taken.
    ///
    /// Read from the bills stored on this till, the same source the per-bill item view uses, so
    /// what a category totals here matches what opening those bills would show. The category comes
    /// from the item's own <c>menu_items</c> row; a line whose item is no longer in the catalog
    /// (renamed, deleted) still counts, filed under "Other".
    /// </summary>
    public IReadOnlyList<CategoryItemSale> GetCategoryItemSales(string startDate, string endDate, long? clientId = null)
    {
        using var conn = _db.OpenConnection();
        return conn.Query<CategoryItemSale>(
            @"SELECT
                  COALESCE(NULLIF(c.name, ''), 'Other')                       AS CategoryName,
                  COALESCE(NULLIF(sc.name, ''), '')                           AS SubCategoryName,
                  COALESCE(mi.category_id, 0)                                 AS CategoryId,
                  COALESCE(NULLIF(oi.item_name, ''), mi.name, 'Item')         AS ItemName,
                  SUM(CASE WHEN oi.quantity > 0 THEN oi.quantity ELSE 1 END)  AS Qty,
                  SUM(CASE WHEN oi.total > 0 THEN oi.total
                           ELSE oi.price * (CASE WHEN oi.quantity > 0 THEN oi.quantity ELSE 1 END) END) AS Amount
              FROM order_items oi
              JOIN orders o          ON o.id = oi.order_id
              LEFT JOIN menu_items mi ON mi.id = oi.item_id
              LEFT JOIN categories c  ON c.id = mi.category_id
              LEFT JOIN categories sc ON sc.id = mi.sub_category_id
              WHERE o.report_visible = 1
                AND o.order_status IN ('settled', 'completed')
                AND date(o.billed_at) >= @startDate
                AND date(o.billed_at) <= @endDate
                AND (@clientId IS NULL OR o.client_id = @clientId)
              GROUP BY CategoryId, CategoryName, SubCategoryName, COALESCE(oi.item_id, oi.item_name)
              ORDER BY CategoryName, Qty DESC, ItemName",
            new { clientId, startDate, endDate }).AsList();
    }
#nullable disable
    public IReadOnlyList<CategoryItemSale> GetCategoryItemSalesForOrders(System.Collections.Generic.IEnumerable<long> orderIds)
    {
        if (orderIds == null || !System.Linq.Enumerable.Any(orderIds))
        {
            return new System.Collections.Generic.List<CategoryItemSale>();
        }
        using var conn = _db.OpenConnection();
        return conn.Query<CategoryItemSale>(
            @"SELECT
                  COALESCE(NULLIF(c.name, ''), 'Other')                       AS CategoryName,
                  COALESCE(NULLIF(sc.name, ''), '')                           AS SubCategoryName,
                  COALESCE(mi.category_id, 0)                                 AS CategoryId,
                  COALESCE(NULLIF(oi.item_name, ''), mi.name, 'Item')         AS ItemName,
                  SUM(CASE WHEN oi.quantity > 0 THEN oi.quantity ELSE 1 END)  AS Qty,
                  SUM(CASE WHEN oi.total > 0 THEN oi.total
                           ELSE oi.price * (CASE WHEN oi.quantity > 0 THEN oi.quantity ELSE 1 END) END) AS Amount
              FROM order_items oi
              JOIN orders o          ON o.id = oi.order_id
              LEFT JOIN menu_items mi ON mi.id = oi.item_id
              LEFT JOIN categories c  ON c.id = mi.category_id
              LEFT JOIN categories sc ON sc.id = mi.sub_category_id
              WHERE o.id IN @orderIds
              GROUP BY CategoryId, CategoryName, SubCategoryName, COALESCE(oi.item_id, oi.item_name)",
            new { orderIds }).AsList();
    }
#nullable restore


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
