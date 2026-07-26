using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Pos.Core.Data;

namespace Pos.Core.Sync;

/// <summary>Outcome of one flush pass, for the status shown on screen.</summary>
public sealed record SyncFlushResult(int Sent, int Failed, int Remaining, string? LastError);

/// <summary>
/// Pushes everything the till has recorded locally up to the server — the outbound half of
/// the SQLite ↔ MySQL coordination. Port of <c>src/storage/sync-service.js</c>.
///
/// The till never waits for this: bills are written to SQLite and queued, and this drains the
/// queue whenever the server is reachable. A row that fails is retried with a widening
/// backoff rather than blocking the ones behind it.
/// </summary>
public sealed class SyncQueueService
{
    private const int BatchSize = 25;

    private readonly DatabaseService _db;
    private readonly PosApiClient _api;

    public SyncQueueService(DatabaseService db, PosApiClient api)
    {
        _db = db;
        _api = api;
        DapperConfig.Init();
    }

    public int PendingCount()
    {
        using var conn = _db.OpenConnection();
        return conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sync_queue WHERE status IN ('pending', 'failed')");
    }

    public async Task<SyncFlushResult> FlushAsync(CancellationToken ct = default)
    {
        List<QueueRow> rows;
        using (var conn = _db.OpenConnection())
        {
            // Every push carries the order's complete current state, so when several are
            // waiting for the same order only the newest is worth sending.
            //
            // This is what keeps a long outage honest. Each failure widens that row's
            // backoff, so an older push can still be waiting while a newer one is already
            // due — and sending them in that order would replay "ordered" on top of a bill
            // the till has since settled. Dropping the stale snapshots removes the race
            // instead of relying on the timing working out.
            conn.Execute(
                @"UPDATE sync_queue
                     SET status = 'superseded', updated_at = datetime('now', '+330 minutes')
                   WHERE status IN ('pending', 'failed')
                     AND EXISTS (SELECT 1 FROM sync_queue newer
                                  WHERE newer.entity_type = sync_queue.entity_type
                                    AND newer.entity_id = sync_queue.entity_id
                                    AND newer.status IN ('pending', 'failed')
                                    AND newer.id > sync_queue.id)");

            // next_attempt_at is IST, so it has to be compared against IST — not UTC.
            rows = conn.Query<QueueRow>(
                @"SELECT id, entity_type, entity_id, operation, payload_json, attempts
                  FROM sync_queue
                  WHERE status IN ('pending', 'failed')
                    AND datetime(next_attempt_at) <= datetime('now', '+330 minutes')
                  ORDER BY id
                  LIMIT @BatchSize", new { BatchSize }).AsList();
        }

        var sent = 0;
        var failed = 0;
        string? lastError = null;

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (RouteFor(row.EntityType, row.Operation, row.EntityId) is not { } route)
                {
                    // Nothing to send on its own — the table's status travels with its order.
                    MarkSynced(row.Id);
                    continue;
                }

                if (row.EntityType is "customer" or "ledger_entry")
                {
                    await PushLedgerAsync(row, ct);
                    MarkSynced(row.Id);
                    sent++;
                    continue;
                }

                if (row.EntityType == "setting")
                {
                    await PushSettingAsync(row, ct);
                    MarkSynced(row.Id);
                    sent++;
                    continue;
                }

                JsonElement? response = null;
                if (route.Method == HttpMethod.Delete)
                {
                    await _api.DeleteAsync(route.Path, ct);
                }
                else if (route.Method == HttpMethod.Put)
                {
                    if (ListPathFor(row.EntityType) is { } listPath)
                    {
                        // A catalog PUT is a full-row overwrite on the server, and this queue
                        // only ever carries the handful of fields the WPF Settings screen
                        // actually edits (name, price, area...). Sending just that would blank
                        // out every column the till doesn't track — a table's QR code, a menu
                        // item's description — the moment its name was changed. Fetching the
                        // row first and overlaying the edit on top keeps everything else intact.
                        await MergeAndPutAsync(listPath, route.Path, row.EntityId, row.PayloadJson ?? "{}", ct);
                    }
                    else
                    {
                        await _api.PutJsonAsync(route.Path, row.PayloadJson ?? "{}", ct);
                    }
                }
                else
                {
                    response = await _api.PostJsonAsync(route.Path, row.PayloadJson ?? "{}", ct);
                }
                MarkSynced(row.Id);
                MarkOrderLiveSynced(row, response);
                sent++;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                MarkFailed(row, ex.Message);
                failed++;
            }
        }

        return new SyncFlushResult(sent, failed, PendingCount(), lastError);
    }

    /// <summary>
    /// Where each queued change goes and which HTTP verb it needs.
    ///
    /// Bills and catalog edits don't share a shape, so they don't share a rule:
    ///  • A quick bill is written once and never changes, so it goes to /sync/orders, which
    ///    dedupes on sqlite_uuid and answers "already synced" if the row is retried.
    ///  • A table order keeps changing — items are added, then it's billed, then settled —
    ///    and /sync/orders would only ever record the first push, leaving the server stuck
    ///    with a half-finished bill. /table-orders updates the table's running order, so
    ///    every push moves the server to the current state (and a retry is harmless).
    ///  • Catalog rows (menu items, categories, tables, areas, GST) only ever reach this
    ///    queue as edits or deletes — creating one is a synchronous, online-only call in
    ///    CatalogRepository so the row is born with the server's own id and never collides
    ///    with an id assigned independently on the other side. So every queued catalog row
    ///    already has a real server id, and PUT/DELETE by that id is always the right verb.
    /// </summary>
    private static (HttpMethod Method, string Path)? RouteFor(string? entityType, string? operation, string? entityId) =>
        (entityType, operation) switch
        {
            ("order", _) => (HttpMethod.Post, "/sync/orders"),
            ("table_order", _) or ("table_state", _) => (HttpMethod.Post, "/table-orders"),
            ("area", "delete") => (HttpMethod.Delete, $"/dining-areas/{entityId}"),
            ("area", _) => (HttpMethod.Put, $"/dining-areas/{entityId}"),
            ("table", "delete") => (HttpMethod.Delete, $"/tables/{entityId}"),
            ("table", _) => (HttpMethod.Put, $"/tables/{entityId}"),
            ("category", "delete") => (HttpMethod.Delete, $"/categories/{entityId}"),
            ("category", _) => (HttpMethod.Put, $"/categories/{entityId}"),
            ("gst_rate", "delete") => (HttpMethod.Delete, $"/gst-rates/{entityId}"),
            ("gst_rate", _) => (HttpMethod.Put, $"/gst-rates/{entityId}"),
            ("menu_item", "delete") => (HttpMethod.Delete, $"/menu-items/{entityId}"),
            ("menu_item", _) => (HttpMethod.Put, $"/menu-items/{entityId}"),
            // Handled by PushLedgerAsync — their URLs need the server's own ids, which this
            // pure lookup can't reach. Listed so they aren't mistaken for "nothing to send".
            ("customer", _) or ("ledger_entry", _) => (HttpMethod.Post, "/ledger"),
            ("setting", _) => (HttpMethod.Put, "/settings"),
            _ => null
        };

    /// <summary>
    /// Pushes one shared setting. The server stores settings as JSON under a key, so the
    /// local value_json is sent through as-is and comes back the same on any other till.
    /// </summary>
    private async Task PushSettingAsync(QueueRow row, CancellationToken ct)
    {
        var payload = JsonNode.Parse(row.PayloadJson ?? "{}") as JsonObject ?? new JsonObject();
        var key = payload["key"]?.GetValue<string>() ?? row.EntityId;
        var raw = payload["value_json"]?.GetValue<string>() ?? "";

        // The value is stored locally as a JSON string; the server wants the parsed value
        // under "value" so it lands in a JSON column rather than as a quoted blob.
        JsonNode? value;
        try { value = JsonNode.Parse(raw); }
        catch (JsonException) { value = JsonValue.Create(raw); }

        var body = new JsonObject { ["value"] = value }.ToJsonString();
        await _api.PutJsonAsync($"/settings/{Uri.EscapeDataString(key ?? "")}", body, ct);
    }

    /// <summary>
    /// Pushes one udhaar-book change.
    ///
    /// The ledger can't go through the plain table above because its URLs are built from the
    /// SERVER's ids, not the till's: an entry is posted to /ledger/customers/{serverId}/entries.
    /// So each customer's server id is recorded locally (customers.live_id) the first time it
    /// syncs, and its entries wait for it — if the customer hasn't landed yet this throws, the
    /// row goes back to the queue with a backoff, and it goes through on a later pass.
    ///
    /// Retries are safe: the server upserts an entry on its uuid, so the same udhaar can never
    /// be counted twice however many times this is attempted.
    /// </summary>
    private async Task PushLedgerAsync(QueueRow row, CancellationToken ct)
    {
        var localId = long.Parse(row.EntityId ?? "0");

        if (row.EntityType == "customer")
        {
            if (row.Operation == "delete")
            {
                if (ServerIdOf("customers", localId) is { } serverId)
                {
                    await _api.DeleteAsync($"/ledger/customers/{serverId}", ct);
                }
                return;
            }

            var existing = ServerIdOf("customers", localId);
            var result = existing is { } id
                ? await _api.PutJsonAsync($"/ledger/customers/{id}", row.PayloadJson ?? "{}", ct)
                : await _api.PostJsonAsync("/ledger/customers", row.PayloadJson ?? "{}", ct);

            if (existing is null && DataId(result) is { } newId)
            {
                RecordServerId("customers", localId, newId);
            }
            return;
        }

        // ── ledger entry ──
        if (row.Operation == "delete")
        {
            if (ServerIdOf("ledger_entries", localId) is { } serverEntryId)
            {
                await _api.DeleteAsync($"/ledger/entries/{serverEntryId}", ct);
            }
            return;
        }

        var patch = JsonNode.Parse(row.PayloadJson ?? "{}") as JsonObject ?? new JsonObject();
        var customerLocalId = (long?)(patch["customer_id"]?.GetValue<long>()) ?? 0;
        var customerServerId = ServerIdOf("customers", customerLocalId)
            ?? throw new InvalidOperationException(
                $"Customer {customerLocalId} abhi server par nahi pahuncha — entry baad me bhejenge.");

        patch["uuid"] = LocalUuid("ledger_entries", localId);
        patch.Remove("customer_id");

        var entryServerId = ServerIdOf("ledger_entries", localId);
        var entryResult = entryServerId is { } eid
            ? await _api.PutJsonAsync($"/ledger/entries/{eid}", patch.ToJsonString(), ct)
            : await _api.PostJsonAsync($"/ledger/customers/{customerServerId}/entries", patch.ToJsonString(), ct);

        if (entryServerId is null && DataId(entryResult) is { } newEntryId)
        {
            RecordServerId("ledger_entries", localId, newEntryId);
        }
    }

    /// <summary>The server's id for a local row, once it has synced at least once.</summary>
    private long? ServerIdOf(string table, long localId)
    {
        using var conn = _db.OpenConnection();
        return conn.ExecuteScalar<long?>($"SELECT live_id FROM \"{table}\" WHERE id = @localId", new { localId });
    }

    private void RecordServerId(string table, long localId, long serverId)
    {
        using var conn = _db.OpenConnection();
        conn.Execute(
            $"UPDATE \"{table}\" SET live_id = @serverId, live_sync_status = 'synced' WHERE id = @localId",
            new { serverId, localId });
    }

    private string? LocalUuid(string table, long localId)
    {
        using var conn = _db.OpenConnection();
        return conn.ExecuteScalar<string?>($"SELECT uuid FROM \"{table}\" WHERE id = @localId", new { localId });
    }

    private static long? DataId(JsonElement? response)
    {
        if (response is not { } root || !root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("id", out var idProp))
        {
            return null;
        }
        return idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var id) ? id
             : long.TryParse(idProp.ToString(), out var parsed) ? parsed : null;
    }

    /// <summary>The list endpoint a catalog entity's rows come from — where <see cref="MergeAndPutAsync"/>
    /// fetches the row to merge onto before sending it back.</summary>
    private static string? ListPathFor(string? entityType) => entityType switch
    {
        "area" => "/dining-areas",
        "table" => "/tables",
        "category" => "/categories",
        "gst_rate" => "/gst-rates",
        "menu_item" => "/menu-items",
        _ => null
    };

    /// <summary>Fetches the row's current server state, overlays the queued edit on top field
    /// by field, and PUTs the merged result — so an edit to one field can never blank out the
    /// others.</summary>
    private async Task MergeAndPutAsync(string listPath, string putPath, string? entityId, string patchJson, CancellationToken ct)
    {
        var list = await _api.GetAsync(listPath, ct);
        var current = FindById(list, entityId);

        var merged = current is JsonObject baseObj ? baseObj : new JsonObject();
        if (JsonNode.Parse(patchJson) is JsonObject patch)
        {
            foreach (var (key, value) in patch)
            {
                merged[key] = value?.DeepClone();
            }
        }

        await _api.PutJsonAsync(putPath, merged.ToJsonString(), ct);
    }

    private static JsonNode? FindById(JsonElement? response, string? id)
    {
        if (response is not { } root || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var row in data.EnumerateArray())
        {
            if (row.TryGetProperty("id", out var idProp) && idProp.ToString() == id)
            {
                return JsonNode.Parse(row.GetRawText());
            }
        }
        return null;
    }

    /// <summary>
    /// Stamps the bill itself as synced once its push lands — live_sync_status/at, plus the
    /// id the server filed it under.
    ///
    /// Without this the column sat on 'pending' for ever, even for bills that had reached
    /// MySQL minutes earlier, because only the queue row was being updated. That is worse
    /// than untidy: the Electron app shares this same SQLite file and its own sync service
    /// picks up orders WHERE live_sync_status IN ('pending','failed',...) — so every bill
    /// this app wrote looked unsent and was a candidate for being pushed a second time.
    /// </summary>
    private void MarkOrderLiveSynced(QueueRow row, JsonElement? response)
    {
        if (row.EntityType is not ("order" or "table_order") || !long.TryParse(row.EntityId, out var orderId))
        {
            return;
        }

        using var conn = _db.OpenConnection();
        conn.Execute(
            @"UPDATE orders
                 SET live_sync_status = 'synced',
                     live_sync_at = datetime('now', '+330 minutes'),
                     live_sync_error = NULL,
                     live_server_id = COALESCE(@serverId, live_server_id)
               WHERE id = @orderId",
            new { orderId, serverId = ServerIdFrom(response) });
    }

    /// <summary>The server's own row id — <c>server_id</c> from /sync/orders, <c>id</c> from
    /// /table-orders. Null when the response doesn't carry one (a clear, say).</summary>
    private static long? ServerIdFrom(JsonElement? response)
    {
        if (response is not { } root || !root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in new[] { "server_id", "id" })
        {
            if (data.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number &&
                v.TryGetInt64(out var id) && id > 0)
            {
                return id;
            }
        }
        return null;
    }

    private void MarkSynced(long id)
    {
        using var conn = _db.OpenConnection();
        using var tx = conn.BeginTransaction();
        conn.Execute(
            @"UPDATE sync_queue SET status = 'synced', last_error = NULL,
                updated_at = datetime('now', '+330 minutes') WHERE id = @id", new { id }, tx);
        conn.Execute(
            @"INSERT INTO sync_logs (queue_id, level, message, created_at)
              VALUES (@id, 'info', 'Synced', datetime('now', '+330 minutes'))", new { id }, tx);
        tx.Commit();
    }

    private void MarkFailed(QueueRow row, string error)
    {
        // Widening backoff so a server that is down doesn't get hammered, capped at an hour.
        var attempts = row.Attempts + 1;
        var delaySeconds = Math.Min(3600, Math.Pow(2, Math.Min(attempts, 8)) * 5);

        using var conn = _db.OpenConnection();
        using var tx = conn.BeginTransaction();
        conn.Execute(
            @"UPDATE sync_queue
              SET status = 'failed', attempts = @attempts, last_error = @error,
                  next_attempt_at = datetime('now', '+330 minutes', @delay),
                  updated_at = datetime('now', '+330 minutes')
              WHERE id = @id",
            new { attempts, error, delay = $"+{(int)delaySeconds} seconds", id = row.Id }, tx);
        conn.Execute(
            @"INSERT INTO sync_logs (queue_id, level, message, created_at)
              VALUES (@id, 'warn', @error, datetime('now', '+330 minutes'))",
            new { id = row.Id, error }, tx);
        tx.Commit();
    }

    private sealed class QueueRow
    {
        public long Id { get; set; }
        public string? EntityType { get; set; }
        public string? EntityId { get; set; }
        public string? Operation { get; set; }
        public string? PayloadJson { get; set; }
        public int Attempts { get; set; }
    }
}
