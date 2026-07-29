using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Pos.Core.Data;
using Pos.Core.Models;

namespace Pos.Core.Repositories;

/// <summary>
/// Port of <c>src/storage/order-repository.js</c>. Owns the KOT / bill / clear
/// order flow against local SQLite, keeping each save atomic (order + items +
/// table state + sync-queue row in one transaction).
/// </summary>
public sealed class OrderRepository
{
    private readonly DatabaseService _db;


    /// <summary>Which business the till is billing for; every client-scoped read and write
    /// below defaults to it so no call site has to remember to pass one.</summary>
    private readonly ClientContext _client;

    public OrderRepository(DatabaseService db, ClientContext client)
    {
        _db = db;
        _client = client;
        DapperConfig.Init();
    }

    /// <summary>
    /// Who is at the till, so every bill records who took it.
    ///
    /// Wired once at startup rather than passed down through each payload: an order is written
    /// from eight different places — KOT, final bill, merge, split, transfer, clear — and a
    /// parameter would eventually be forgotten at one of them. That one is then the operator
    /// whose sales quietly go missing from their own report.
    /// </summary>
    public Func<long?>? CurrentUserId { get; set; }

    /// <summary>Stamps the operator onto the payload before anything is written, so the row and
    /// the sync body that follows it can't disagree about who billed.</summary>
    private void StampOperator(TableOrderPayload payload) =>
        payload.CreatedBy ??= CurrentUserId?.Invoke();

    // ── Reads ────────────────────────────────────────────────────────────────

    /// <summary>Latest non-settled order (with items) backing a table, or null.</summary>
    public Order? GetActiveOrderForTable(long tableId, long? clientId = null)
    {
        clientId ??= _client.ClientId;
        using var conn = _db.OpenConnection();
        var order = conn.QueryFirstOrDefault<Order>(
            @"SELECT o.*, rt.table_number
              FROM orders o
              LEFT JOIN restaurant_tables rt ON rt.id = o.table_id
              WHERE o.table_id = @tableId AND o.client_id = @clientId
                AND o.order_status NOT IN ('cancelled', 'settled')
              ORDER BY o.id DESC LIMIT 1",
            new { tableId, clientId });
        if (order == null)
        {
            return null;
        }
        order.Items = conn.Query<OrderItem>(
            "SELECT * FROM order_items WHERE order_id = @id ORDER BY id", new { id = order.Id }).AsList();
        return order;
    }

    public NextBillNumberResult NextBillNumber(long? clientId = null)
    {
        clientId ??= _client.ClientId;
        using var conn = _db.OpenConnection();
        return NextBillNumberInternal(conn, null, clientId.Value);
    }

    // ── KOT / Bill / Clear ───────────────────────────────────────────────────

    /// <summary>
    /// Port of <c>saveFinalOrder</c>: records a finished bill that belongs to no table
    /// (a quick bill). Always INSERTs a new order with a fresh bill number and never
    /// touches table state — putting quick bills through <see cref="SaveTableOrder"/>
    /// instead is what used to park them on the first table.
    /// </summary>
    public SaveOrderResult SaveFinalOrder(TableOrderPayload payload, long? clientId = null, string orderStatus = "settled")
    {
        clientId ??= _client.ClientId;
        StampOperator(payload);

        using var conn = _db.OpenConnection();
        using var tx = conn.BeginTransaction();

        var items = payload.Items ?? new List<OrderItemInput>();
        var total = payload.TotalAmount ?? ItemsTotal(items);
        // A new row every time, so the number has to be one nobody else holds.
        var billNumber = ResolveUniqueBillNumber(conn, tx, clientId.Value,
            payload.BillNumber ?? NextBillNumberInternal(conn, tx, clientId.Value).BillNumber, null);

        conn.Execute(
            @"INSERT INTO orders
               (client_id, table_id, customer_id, created_by, order_status, total_amount,
                discount_amount, discount_type, discount_value, discount_label,
                customer_name, customer_mobile, bill_note, is_kot_only, report_visible,
                billed_at, bill_number, live_sync_status, is_parcel_mode, created_at, updated_at)
              VALUES (@clientId, @tableId, NULL, @createdBy, @orderStatus, @total,
                @discountAmount, @discountType, @discountValue, @discountLabel,
                @customerName, @customerMobile, @billNote, 0, 1,
                COALESCE(@billedAt, datetime('now', '+330 minutes')), @billNumber, 'pending',
                @isParcelMode, datetime('now', '+330 minutes'), datetime('now', '+330 minutes'))",
            new
            {
                clientId,
                tableId = payload.TableId,          // null for a quick bill
                createdBy = payload.CreatedBy,
                orderStatus, total,
                discountAmount = payload.DiscountAmount, discountType = TextOrNull(payload.DiscountType),
                discountValue = payload.DiscountValue, discountLabel = TextOrNull(payload.DiscountLabel),
                customerName = TextOrNull(payload.CustomerName), customerMobile = TextOrNull(payload.CustomerMobile),
                billNote = TextOrNull(payload.BillNote),
                billedAt = TextOrNull(NormalizeBilledAtToIst(payload.BilledAt)),
                billNumber,
                isParcelMode = payload.IsParcelMode ? 1 : 0
            }, tx);

        var orderId = conn.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: tx);
        ReadableUuid.Stamp(conn, tx, "orders", ReadableUuid.Order, orderId);

        ReplaceItems(conn, tx, orderId, items);
        LogStatus(conn, tx, orderId, orderStatus);
        Enqueue(conn, tx, "order", orderId.ToString(), "upsert",
            SyncBody(clientId.Value, orderId, OrderUuid(conn, tx, orderId), payload.TableId, billNumber, total, null,
                     orderStatus, payload, items));

        tx.Commit();
        return new SaveOrderResult
        {
            Success = true, Id = orderId, BillNumber = billNumber, TableId = payload.TableId,
            OrderStatus = orderStatus, TotalAmount = total
        };
    }

    public SaveOrderResult SaveTableOrder(TableOrderPayload payload, long? clientId = null)
    {
        clientId ??= _client.ClientId;
        StampOperator(payload);

        using var conn = _db.OpenConnection();
        using var tx = conn.BeginTransaction();

        var tableId = ResolveTableId(payload.TableId);
        var items = payload.Items ?? new List<OrderItemInput>();
        var tableStatus = string.IsNullOrWhiteSpace(payload.TableStatus) ? "ordered" : payload.TableStatus;
        var orderStatus = UiStatusToOrderStatus(tableStatus);
        var total = payload.TotalAmount ?? ItemsTotal(items);

        // Clear path: available + no items → settle the running order, free table.
        if (tableStatus == "available" && items.Count == 0)
        {
            var existingClear = conn.QueryFirstOrDefault<long?>(
                @"SELECT o.id FROM orders o
                  WHERE o.table_id = @tableId AND o.client_id = @clientId
                    AND o.order_status NOT IN ('cancelled', 'settled')
                  ORDER BY o.id DESC LIMIT 1", new { tableId, clientId }, tx);

            if (existingClear.HasValue)
            {
                conn.Execute(
                    @"UPDATE orders SET order_status = 'settled', sync_version = sync_version + 1,
                        updated_at = datetime('now', '+330 minutes'), live_sync_status = 'pending'
                      WHERE id = @id AND client_id = @clientId",
                    new { id = existingClear.Value, clientId }, tx);
                LogStatus(conn, tx, existingClear.Value, "settled");
            }

            UpdateTableState(conn, tx, tableId, clientId.Value, "available", 0, null);
            // items must be present (even empty) — that empty list is exactly how the
            // server is told "this table is done, settle its running order".
            Enqueue(conn, tx, "table_state", tableId.ToString(), "upsert",
                new { table_id = tableId, table_status = "available", items = System.Array.Empty<object>() });
            tx.Commit();
            return new SaveOrderResult
            {
                Success = true, Id = existingClear, Cleared = true, TableId = tableId,
                TableStatus = "available", TotalAmount = 0
            };
        }

        var existing = conn.QueryFirstOrDefault<ExistingOrderRow>(
            @"SELECT o.id, o.bill_number
              FROM orders o
              WHERE o.table_id = @tableId AND o.client_id = @clientId
                AND o.order_status NOT IN ('cancelled', 'settled')
              ORDER BY o.id DESC LIMIT 1", new { tableId, clientId }, tx);
        var hasExisting = existing != null;

        var isFinalBill = (tableStatus is "available" or "complete" or "completed") && items.Count > 0;
        long? billNumber = isFinalBill
            ? ResolveUniqueBillNumber(conn, tx, clientId.Value,
                existing?.BillNumber ?? payload.BillNumber ?? NextBillNumberInternal(conn, tx, clientId.Value).BillNumber,
                hasExisting ? existing!.Id : null)
            : null;

        long orderId;
        if (hasExisting)
        {
            orderId = existing!.Id;
            conn.Execute(
                @"UPDATE orders
                  SET order_status = @orderStatus, total_amount = @total, discount_amount = @discountAmount,
                      discount_type = @discountType, discount_value = @discountValue, discount_label = @discountLabel,
                      customer_name = @customerName, customer_mobile = @customerMobile, bill_note = @billNote,
                      is_kot_only = @isKotOnly, report_visible = @reportVisible, billed_at = @billedAt,
                      bill_number = COALESCE(@billNumber, bill_number), is_parcel_mode = @isParcelMode,
                      sync_version = sync_version + 1, updated_at = datetime('now', '+330 minutes'),
                      live_sync_status = @liveSyncStatus
                  WHERE id = @orderId AND client_id = @clientId",
                new
                {
                    orderStatus, total,
                    discountAmount = payload.DiscountAmount, discountType = TextOrNull(payload.DiscountType),
                    discountValue = payload.DiscountValue, discountLabel = TextOrNull(payload.DiscountLabel),
                    customerName = TextOrNull(payload.CustomerName), customerMobile = TextOrNull(payload.CustomerMobile),
                    billNote = TextOrNull(payload.BillNote),
                    isKotOnly = isFinalBill ? 0 : 1,
                    reportVisible = isFinalBill ? 1 : 0,
                    billedAt = isFinalBill ? (TextOrNull(NormalizeBilledAtToIst(payload.BilledAt)) ?? IstNow()) : null,
                    billNumber,
                    isParcelMode = payload.IsParcelMode ? 1 : 0,
                    liveSyncStatus = isFinalBill ? "pending" : "not_applicable",
                    orderId, clientId
                }, tx);
        }
        else
        {
            conn.Execute(
                @"INSERT INTO orders
                   (client_id, table_id, created_by, order_status, total_amount, discount_amount,
                    discount_type, discount_value, discount_label, customer_name, customer_mobile,
                    bill_note, is_kot_only, report_visible, billed_at, bill_number, live_sync_status,
                    is_parcel_mode, created_at, updated_at)
                  VALUES (@clientId, @tableId, @createdBy, @orderStatus, @total, @discountAmount,
                    @discountType, @discountValue, @discountLabel, @customerName, @customerMobile,
                    @billNote, @isKotOnly, @reportVisible, @billedAt, @billNumber, @liveSyncStatus,
                    @isParcelMode, datetime('now', '+330 minutes'), datetime('now', '+330 minutes'));",
                new
                {
                    clientId, tableId, orderStatus, total,
                    createdBy = payload.CreatedBy,
                    discountAmount = payload.DiscountAmount, discountType = TextOrNull(payload.DiscountType),
                    discountValue = payload.DiscountValue, discountLabel = TextOrNull(payload.DiscountLabel),
                    customerName = TextOrNull(payload.CustomerName), customerMobile = TextOrNull(payload.CustomerMobile),
                    billNote = TextOrNull(payload.BillNote),
                    isKotOnly = isFinalBill ? 0 : 1,
                    reportVisible = isFinalBill ? 1 : 0,
                    billedAt = isFinalBill ? (TextOrNull(NormalizeBilledAtToIst(payload.BilledAt)) ?? IstNow()) : null,
                    billNumber,
                    liveSyncStatus = isFinalBill ? "pending" : "not_applicable",
                    isParcelMode = payload.IsParcelMode ? 1 : 0
                }, tx);
            orderId = conn.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: tx);
            ReadableUuid.Stamp(conn, tx, "orders", ReadableUuid.Order, orderId);
        }

        var finalTotal = total;
        if (items.Count > 0)
        {
            var replace = !payload.MergeItems;
            if (!replace && hasExisting)
            {
                var existingItems = conn.Query<OrderItem>(
                    "SELECT * FROM order_items WHERE order_id = @orderId", new { orderId }, tx).AsList();
                if (existingItems.Count > 0)
                {
                    var merged = MergeOrderItems(existingItems.Select(ToInput).ToList(), items);
                    finalTotal = ItemsTotal(merged);
                    conn.Execute(
                        @"UPDATE orders SET total_amount = @finalTotal, sync_version = sync_version + 1,
                            updated_at = datetime('now', '+330 minutes') WHERE id = @orderId",
                        new { finalTotal, orderId }, tx);
                    ReplaceItems(conn, tx, orderId, merged);
                }
                else
                {
                    ReplaceItems(conn, tx, orderId, items);
                }
            }
            else
            {
                ReplaceItems(conn, tx, orderId, items);
            }
        }

        LogStatus(conn, tx, orderId, orderStatus);

        // Preserve the original order-start timestamp so the table's elapsed clock
        // doesn't reset on every KOT/bill re-save.
        var existingTs = conn.QueryFirstOrDefault<long?>(
            "SELECT order_timestamp FROM table_client_states WHERE table_id = @tableId AND client_id = @clientId",
            new { tableId, clientId }, tx);
        var isAvailable = tableStatus == "available";
        UpdateTableState(conn, tx, tableId, clientId.Value, tableStatus,
            isAvailable ? 0 : finalTotal,
            isAvailable ? null : (existingTs ?? payload.OrderTimestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        // Read the items back so a merge-mode save syncs what actually ended up stored.
        var syncItems = conn.Query<OrderItem>(
            "SELECT * FROM order_items WHERE order_id = @orderId ORDER BY id", new { orderId }, tx)
            .Select(ToInput).ToList();
        Enqueue(conn, tx, "table_order", orderId.ToString(), "upsert",
            SyncBody(clientId.Value, orderId, OrderUuid(conn, tx, orderId), tableId, billNumber, finalTotal, tableStatus,
                     orderStatus, payload, syncItems));

        tx.Commit();
        return new SaveOrderResult
        {
            Success = true, Id = orderId, BillNumber = billNumber, TableId = tableId,
            OrderStatus = orderStatus, TableStatus = tableStatus, TotalAmount = finalTotal
        };
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private NextBillNumberResult NextBillNumberInternal(SqliteConnection conn, IDbTransaction? tx, long clientId)
    {
        var dailyReset = false;
        try
        {
            var json = conn.QueryFirstOrDefault<string>(
                "SELECT value_json FROM app_settings WHERE key = 'daily_reset_bill_counter' LIMIT 1", null, tx);
            if (!string.IsNullOrEmpty(json))
            {
                var val = json.Trim().Trim('"').ToLowerInvariant();
                dailyReset = val is "true" or "1";
            }
        }
        catch { /* settings may be missing → no reset */ }

        var next = dailyReset
            ? conn.ExecuteScalar<long>(
                "SELECT COALESCE(MAX(bill_number), 0) + 1 FROM orders WHERE client_id = @clientId AND date(billed_at) = date('now', '+330 minutes')",
                new { clientId }, tx)
            : conn.ExecuteScalar<long>(
                "SELECT COALESCE(MAX(bill_number), 0) + 1 FROM orders WHERE client_id = @clientId",
                new { clientId }, tx);
        next = Math.Max(1, next);
        next = ResolveUniqueBillNumber(conn, tx, clientId, next, null) ?? next;

        var prefix = "DR";
        try
        {
            var client = conn.QueryFirstOrDefault<ClientRow>(
                "SELECT slug, name FROM clients WHERE id = @clientId", new { clientId }, tx);
            var slug = (client?.Slug ?? "").ToLowerInvariant();
            if (slug.Contains("chaychaupal") || slug.Contains("chay") || slug.Contains("cc"))
            {
                prefix = "CC";
            }
            else if (slug.Contains("daalroti") || slug.Contains("roti") || slug.Contains("dr"))
            {
                prefix = "DR";
            }
            else if (!string.IsNullOrWhiteSpace(client?.Name))
            {
                var words = client!.Name!.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                prefix = words.Length >= 2
                    ? (string.Concat(words[0][0], words[1][0])).ToUpperInvariant()
                    : client.Name!.Substring(0, Math.Min(2, client.Name!.Length)).ToUpperInvariant();
            }
        }
        catch { /* keep default prefix */ }

        return new NextBillNumberResult
        {
            NextBillNumber = next,
            BillNumber = next,
            BillPrefix = prefix,
            FormattedBillNumber = $"#{prefix}-{next.ToString().PadLeft(4, '0')}"
        };
    }

    private static long? ResolveUniqueBillNumber(SqliteConnection conn, IDbTransaction? tx, long clientId, long? candidate, long? excludeOrderId)
    {
        if (candidate == null)
        {
            return null;
        }
        var clashId = conn.QueryFirstOrDefault<long?>(
            "SELECT id FROM orders WHERE client_id = @clientId AND bill_number = @candidate LIMIT 1",
            new { clientId, candidate }, tx);
        if (clashId == null || (excludeOrderId != null && clashId.Value == excludeOrderId.Value))
        {
            return candidate;
        }
        var fallback = conn.ExecuteScalar<long>(
            "SELECT COALESCE(MAX(bill_number), 0) + 1 FROM orders WHERE client_id = @clientId", new { clientId }, tx);
        return Math.Max(1, fallback);
    }

    /// <summary>
    /// A table order needs a real table. This used to fall back to the first table in the
    /// database when the id was missing, which silently parked table-less (quick) bills on
    /// table 1 and marked it occupied. Anything without a table belongs in
    /// <see cref="SaveFinalOrder"/>, so a missing id is a bug worth surfacing.
    /// </summary>
    private static long ResolveTableId(long? rawTableId)
    {
        if (rawTableId is null or 0)
        {
            throw new InvalidOperationException(
                "SaveTableOrder needs a table id. A bill with no table must go through SaveFinalOrder.");
        }
        return rawTableId.Value;
    }

    private static void ReplaceItems(SqliteConnection conn, IDbTransaction tx, long orderId, IEnumerable<OrderItemInput> items)
    {
        conn.Execute("DELETE FROM order_items WHERE order_id = @orderId", new { orderId }, tx);
        foreach (var item in items)
        {
            var qty = Math.Max(1, item.Quantity);
            var price = item.Price;
            var disc = item.DiscountAmount;
            conn.Execute(
                @"INSERT INTO order_items
                   (order_id, item_id, client_item_id, item_name, price, quantity, is_parcel, total,
                    discount_amount, discount_type, discount_value, discount_label, created_at, updated_at)
                  VALUES (@orderId, @itemId, @clientItemId, @itemName, @price, @qty, @isParcel, @total,
                    @disc, @discType, @discValue, @discLabel, datetime('now', '+330 minutes'), datetime('now', '+330 minutes'))",
                new
                {
                    orderId,
                    itemId = item.ItemId,
                    clientItemId = TextOrNull(item.ClientItemId),
                    itemName = TextOrNull(item.ItemName),
                    price, qty,
                    isParcel = item.IsParcel ? 1 : 0,
                    total = Math.Max(0, price * qty - disc),
                    disc,
                    discType = TextOrNull(item.DiscountType),
                    discValue = item.DiscountValue,
                    discLabel = TextOrNull(item.DiscountLabel)
                }, tx);

            ReadableUuid.Stamp(conn, tx, "order_items", ReadableUuid.OrderItem,
                conn.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: tx));
        }
    }

    private static void UpdateTableState(SqliteConnection conn, IDbTransaction tx, long tableId, long clientId, string status, double amount, long? timestamp)
    {
        var isAvailable = status == "available";
        conn.Execute(
            @"INSERT INTO table_client_states (client_id, table_id, table_status, current_amount, order_timestamp, updated_at)
              VALUES (@clientId, @tableId, @status, @amount, @timestamp, datetime('now', '+330 minutes'))
              ON CONFLICT(client_id, table_id) DO UPDATE SET
                table_status = excluded.table_status,
                current_amount = excluded.current_amount,
                order_timestamp = excluded.order_timestamp,
                updated_at = datetime('now', '+330 minutes')",
            new { clientId, tableId, status, amount = isAvailable ? 0 : amount, timestamp = isAvailable ? (long?)null : timestamp }, tx);

        conn.Execute(
            @"UPDATE restaurant_tables
              SET table_status = @status, current_amount = @amount, order_timestamp = @timestamp,
                  updated_at = datetime('now', '+330 minutes')
              WHERE id = @tableId",
            new { status, amount = isAvailable ? 0 : amount, timestamp = isAvailable ? (long?)null : timestamp, tableId }, tx);
    }

    private static void LogStatus(SqliteConnection conn, IDbTransaction tx, long orderId, string status)
    {
        var last = conn.QueryFirstOrDefault<string>(
            "SELECT status FROM order_status_logs WHERE order_id = @orderId ORDER BY id DESC LIMIT 1",
            new { orderId }, tx);
        if (last == status)
        {
            return;
        }
        conn.Execute(
            "INSERT INTO order_status_logs (order_id, status, changed_by, created_at) VALUES (@orderId, @status, NULL, datetime('now', '+330 minutes'))",
            new { orderId, status }, tx);
    }

    /// <summary>
    /// The body the server needs to recreate this order. The queue used to carry only a
    /// summary (id / table / total), which the API rejects outright — it requires the items
    /// — so nothing could ever have synced. Field names are snake_case to match the API.
    /// </summary>
    private static Dictionary<string, object?> SyncBody(
        long clientId,
        long orderId, string sqliteUuid, long? tableId, long? billNumber, double total, string? tableStatus,
        string orderStatus, TableOrderPayload payload, IEnumerable<OrderItemInput> items) =>
        new()
        {
            ["id"] = orderId,
            // The server keys off this to stay duplicate-safe: a retried push finds the
            // order already there instead of billing the customer twice.
            ["sqlite_uuid"] = sqliteUuid,
            ["created_at"] = IstNow(),
            ["updated_at"] = IstNow(),
            // Whose bill this is. Hard-coded to 1 while the till served one brand; with Daal
            // Roti and Chay Chaupal sharing the counter that would file every Chay Chaupal bill
            // under Daal Roti the moment it reached the server.
            ["client_id"] = clientId,
            // Order::syncFromLocal on the server already persists this; without it the
            // dashboard's reports would credit nobody while the till's credit the operator.
            ["created_by"] = payload.CreatedBy,
            ["table_id"] = tableId,
            ["table_status"] = tableStatus,
            ["order_status"] = orderStatus,
            ["bill_number"] = billNumber,
            ["total_amount"] = total,
            ["discount_amount"] = payload.DiscountAmount,
            ["discount_type"] = TextOrNull(payload.DiscountType),
            ["discount_value"] = payload.DiscountValue,
            ["discount_label"] = TextOrNull(payload.DiscountLabel),
            ["customer_name"] = TextOrNull(payload.CustomerName),
            ["customer_mobile"] = TextOrNull(payload.CustomerMobile),
            ["bill_note"] = TextOrNull(payload.BillNote),
            ["is_parcel_mode"] = payload.IsParcelMode ? 1 : 0,
            ["billed_at"] = TextOrNull(NormalizeBilledAtToIst(payload.BilledAt)) ?? IstNow(),
            ["items"] = items.Select(i => new Dictionary<string, object?>
            {
                ["item_id"] = i.ItemId,
                ["client_item_id"] = TextOrNull(i.ClientItemId),
                ["item_name"] = TextOrNull(i.ItemName),
                ["price"] = i.Price,
                ["quantity"] = Math.Max(1, i.Quantity),
                ["is_parcel"] = i.IsParcel ? 1 : 0,
                ["total"] = Math.Max(0, i.Price * Math.Max(1, i.Quantity) - i.DiscountAmount),
                ["discount_amount"] = i.DiscountAmount,
                ["discount_type"] = TextOrNull(i.DiscountType),
                ["discount_value"] = i.DiscountValue,
                ["discount_label"] = TextOrNull(i.DiscountLabel)
            }).ToList()
        };

    /// <summary>The row's own uuid — the key the server dedupes pushes on.</summary>
    private static string OrderUuid(SqliteConnection conn, IDbTransaction tx, long orderId) =>
        conn.QueryFirstOrDefault<string>("SELECT uuid FROM orders WHERE id = @orderId", new { orderId }, tx) ?? "";

    private static void Enqueue(SqliteConnection conn, IDbTransaction tx, string entityType, string entityId, string operation, object payload)
    {
        conn.Execute(
            @"INSERT INTO sync_queue (entity_type, entity_id, operation, payload_json, status)
              VALUES (@entityType, @entityId, @operation, @payloadJson, 'pending')",
            new { entityType, entityId, operation, payloadJson = JsonSerializer.Serialize(payload) }, tx);
    }

    private static List<OrderItemInput> MergeOrderItems(List<OrderItemInput> existing, List<OrderItemInput> incoming)
    {
        var merged = new List<OrderItemInput>();
        var positions = new Dictionary<string, int>();

        foreach (var item in existing)
        {
            var key = OrderItemKey(item);
            if (key == "|dine")
            {
                key = "existing-" + merged.Count;
            }
            positions[key] = merged.Count;
            item.Quantity = Math.Max(1, item.Quantity);
            merged.Add(item);
        }

        foreach (var item in incoming)
        {
            var key = OrderItemKey(item);
            if (key != "|dine" && positions.TryGetValue(key, out var pos))
            {
                var newQty = merged[pos].Quantity + Math.Max(1, item.Quantity);
                merged[pos].Quantity = newQty;
                merged[pos].Price = item.Price != 0 ? item.Price : merged[pos].Price;
            }
            else
            {
                var k = string.IsNullOrEmpty(key) ? "incoming-" + merged.Count : key;
                positions[k] = merged.Count;
                item.Quantity = Math.Max(1, item.Quantity);
                merged.Add(item);
            }
        }

        return merged;
    }

    private static string OrderItemKey(OrderItemInput item)
    {
        var itemId = item.ItemId?.ToString() ?? "";
        var clientItemId = item.ClientItemId ?? "";
        var name = (item.ItemName ?? "").ToLowerInvariant().Trim();
        var parcel = item.IsParcel ? "parcel" : "dine";
        var id = itemId != "" ? itemId : (clientItemId != "" ? clientItemId : name);
        return id + "|" + parcel;
    }

    private static OrderItemInput ToInput(OrderItem i) => new()
    {
        ItemId = i.ItemId,
        ClientItemId = i.ClientItemId,
        ItemName = i.ItemName,
        Price = i.Price,
        Quantity = i.Quantity,
        IsParcel = i.IsParcel != 0,
        DiscountAmount = i.DiscountAmount,
        DiscountType = i.DiscountType,
        DiscountValue = i.DiscountValue,
        DiscountLabel = i.DiscountLabel
    };

    private static double ItemsTotal(IEnumerable<OrderItemInput> items)
        => items.Sum(i => i.Price * Math.Max(1, i.Quantity));

    /// <summary>
    /// An order is only ever in one of three states: <c>ordered</c> (running), then
    /// <c>completed</c> (bill printed), then <c>settled</c> (closed). "available" isn't an
    /// order state at all — it's what a table looks like once its order has been settled.
    ///
    /// The extra states this used to invent (confirmed / pending / occupied) meant the same
    /// situation could be written under different names depending on which button was
    /// pressed, and anything reading them had to guess.
    /// </summary>
    private static string UiStatusToOrderStatus(string status) => status switch
    {
        "complete" or "completed" => "completed",
        "available" or "settled" => "settled",
        _ => "ordered"
    };

    private static string IstNow() => DateTime.UtcNow.AddMinutes(330).ToString("yyyy-MM-dd HH:mm:ss");

    private static string? NormalizeBilledAtToIst(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }
        if (!value.Contains('T') && !value.Contains('Z'))
        {
            return value;
        }
        if (DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d))
        {
            return d.AddMinutes(330).ToString("yyyy-MM-dd HH:mm:ss");
        }
        return value;
    }

    private static string? TextOrNull(string? value)
    {
        var t = (value ?? "").Trim();
        return t.Length == 0 ? null : t;
    }

    private sealed class ExistingOrderRow
    {
        public long Id { get; set; }
        public long? BillNumber { get; set; }
    }

    private sealed class ClientRow
    {
        public string? Slug { get; set; }
        public string? Name { get; set; }
    }
}
