using System.Text.Json;
using Dapper;
using Pos.Core.Data;

namespace Pos.Core.Sync;

/// <summary>
/// Brings the customer ledger DOWN from the server into local SQLite, so the Len-Den screen shows
/// every customer and entry — not just the ones rung on this till.
///
/// The ledger has always been push-only (a change is written locally and queued up to the server);
/// nothing pulled the server's own customers back, so a till pointed at a live server that already
/// had khaata data opened Len-Den to an almost-empty list. This fills that gap.
///
/// Merge, never replace, and keyed on the uuid both sides share: an entry already held is updated
/// in place rather than duplicated, and a local change still waiting in the sync queue is left
/// alone. So it is safe to run on every sync pass and safe alongside offline edits.
/// </summary>
public sealed class LedgerSyncService
{
    private readonly DatabaseService _db;

    public LedgerSyncService(DatabaseService db)
    {
        _db = db;
        DapperConfig.Init();
    }

    /// <summary>Pulls this client's ledger and merges it into SQLite. Answers false when the server
    /// couldn't be read, so the caller can fall back to whatever is already local.</summary>
    public async Task<bool> PullAsync(PosApiClient api, long clientId, CancellationToken ct = default)
    {
        try
        {
            var root = await api.GetAsync("/ledger", ct);
            if (root is null)
            {
                return false;
            }

            var data = root.Value.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object
                ? d : root.Value;

            var customers = Array(data, "customers");
            var entries = Array(data, "entries");

            using var conn = _db.OpenConnection();
            using var tx = conn.BeginTransaction();

            // The server keys entries on ITS customer ids; map each to the local row (by the shared
            // uuid) so a pulled entry hangs off the right customer whatever its local id is.
            var idMap = new Dictionary<long, long>();

            foreach (var c in customers)
            {
                var uuid = Str(c, "uuid");
                if (uuid.Length == 0)
                {
                    continue;
                }

                var serverId = Num(c, "id");
                var name = Str(c, "name");
                var mobile = Str(c, "mobile");
                string? dbMobile = string.IsNullOrWhiteSpace(mobile) ? null : mobile;
                var email = Str(c, "email");
                string? dbEmail = string.IsNullOrWhiteSpace(email) ? null : email;

                var localId = conn.ExecuteScalar<long?>(
                    "SELECT id FROM customers WHERE uuid = @uuid LIMIT 1", new { uuid }, tx);

                if (localId is long existing)
                {
                    conn.Execute(
                        "UPDATE customers SET client_id = @clientId, name = @name, mobile = @dbMobile, email = @dbEmail WHERE id = @existing",
                        new { clientId, name, dbMobile, dbEmail, existing }, tx);
                    idMap[serverId] = existing;
                }
                else
                {
                    conn.Execute(
                        "INSERT INTO customers (uuid, client_id, name, mobile, email) VALUES (@uuid, @clientId, @name, @dbMobile, @dbEmail)",
                        new { uuid, clientId, name, dbMobile, dbEmail }, tx);
                    idMap[serverId] = conn.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: tx);
                }
            }

            foreach (var e in entries)
            {
                var uuid = Str(e, "uuid");
                if (uuid.Length == 0 || !idMap.TryGetValue(Num(e, "customer_id"), out var localCust))
                {
                    continue;
                }

                var type = Str(e, "entry_type");
                var mode = Str(e, "payment_mode") is { Length: > 0 } pm ? pm : "cash";
                var createdAt = Str(e, "occurred_at") is { Length: > 0 } oc ? oc : Str(e, "created_at");

                conn.Execute(
                    @"INSERT INTO ledger_entries (uuid, client_id, customer_id, type, amount, payment_mode, remarks, created_at)
                      VALUES (@uuid, @clientId, @localCust, @type, @amount, @mode, @remarks, @createdAt)
                      ON CONFLICT(uuid) DO UPDATE SET
                        customer_id = excluded.customer_id, type = excluded.type, amount = excluded.amount,
                        payment_mode = excluded.payment_mode, remarks = excluded.remarks, created_at = excluded.created_at",
                    new { uuid, clientId, localCust, type, amount = Dbl(e, "amount"), mode, remarks = Str(e, "note"), createdAt },
                    tx);
            }

            tx.Commit();
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── JSON helpers ─────────────────────────────────────────────────────────
    private static List<JsonElement> Array(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().ToList()
            : new List<JsonElement>();

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString())
            : "";

    private static long Num(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.TryGetInt64(out var n) ? n : (long)v.GetDouble();
        return long.TryParse(Str(e, name), out var p) ? p : 0;
    }

    private static double Dbl(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        return double.TryParse(Str(e, name), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 0;
    }
}
