using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Pos.Core.Data;

/// <summary>
/// Gives every row a key a person can read — POS1-ORD-00003 instead of
/// 0a6d9af2-ed33-4d4b-8fda-9b5ea27a190c.
///
/// The point is matching the two databases by eye: the same string names the same row in
/// SQLite and in MySQL, so a bill, an item or a table can be followed across both without
/// copying 36 characters around.
///
/// The till code is what keeps the keys unique. Every counter runs its own SQLite with its
/// own row ids, so two counters would otherwise mint POS1-ORD-00003 for two different bills
/// and the server would treat them as one. Add a counter → give it its own
/// <c>app_settings['pos_till_code']</c>.
/// </summary>
public static class ReadableUuid
{
    /// <summary>Last resort when the business has no name saved yet.</summary>
    public const string DefaultTillCode = "POS1";

    // Short codes, one per table, so a key says what it points at.
    //
    // Only rows the till creates are listed here. Master data — menu items, categories,
    // tables, areas, GST — is the server's: those uuids arrive with the bootstrap pull and
    // are already identical in both databases, so rewriting them locally would only break
    // the match.
    public const string Order = "ORD";
    public const string OrderItem = "OITM";

    // The ledger's keys matter for more than reading: the server upserts a ledger entry on
    // its uuid, so this is what stops a retried push from counting the same udhaar twice.
    public const string Customer = "CUST";
    public const string LedgerEntry = "LDGR";

    /// <summary>
    /// The prefix every key starts with: four letters of the business name plus the counter
    /// number — DALR1 for the first counter at Dal Roti.
    ///
    /// The name says whose bill it is at a glance, which matters once more than one business
    /// syncs to the same server. The number is what keeps the keys unique: each counter has
    /// its own SQLite and its own row ids, so counter two must be told it is
    /// <c>app_settings['pos_till_code'] = DALR2</c>, or both would mint DALR1-ORD-00003.
    /// </summary>
    public static string TillCode(SqliteConnection conn, IDbTransaction? tx = null)
    {
        var till = conn.ExecuteScalar<string>(
            "SELECT value_json FROM app_settings WHERE key = 'pos_till_code' LIMIT 1", transaction: tx);
        return string.IsNullOrWhiteSpace(till) ? BusinessCode(conn, tx) + "1" : till.Trim().Trim('"');
    }

    /// <summary>
    /// Four characters of the business name, letters and digits only — "Dal Roti" → DALR,
    /// "Chay Chaupal" → CHAY. Padded if the name is shorter, so every key is the same shape.
    /// </summary>
    public static string BusinessCode(SqliteConnection conn, IDbTransaction? tx = null)
    {
        var name = ProfileName(conn, tx)
                   ?? conn.ExecuteScalar<string>("SELECT name FROM clients ORDER BY id LIMIT 1", transaction: tx);

        var letters = new string((name ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return letters.Length >= 4 ? letters[..4]
             : letters.Length > 0 ? letters.PadRight(4, 'X')
             : DefaultTillCode;
    }

    /// <summary>
    /// The store name, from whichever profile has one — this app's own Settings screen first
    /// (<c>pos_wpf_settings.StoreName</c>), then the Electron app's older
    /// <c>restaurant_profile.name</c> in the same database.
    ///
    /// Reading only the Electron key, as this used to, meant renaming the store on the
    /// Settings screen changed the name on every bill but not the prefix on the keys.
    /// </summary>
    private static string? ProfileName(SqliteConnection conn, IDbTransaction? tx)
    {
        return Read("pos_wpf_settings", "StoreName") ?? Read("restaurant_profile", "name");

        string? Read(string key, string property)
        {
            var json = conn.ExecuteScalar<string>(
                "SELECT value_json FROM app_settings WHERE key = @key LIMIT 1", new { key }, tx);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var value = doc.RootElement.TryGetProperty(property, out var n) ? n.GetString() : null;
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            catch (JsonException)
            {
                // A hand-edited profile shouldn't stop a bill from being saved.
                return null;
            }
        }
    }

    /// <summary>
    /// Rewrites one freshly inserted row's uuid. Call it straight after the insert, inside the
    /// same transaction, so the row is never visible with the random default.
    /// </summary>
    public static void Stamp(SqliteConnection conn, IDbTransaction? tx, string table, string code, long id)
    {
        conn.Execute(
            $@"UPDATE ""{table}"" SET uuid = @prefix || substr('00000' || id, -5) WHERE id = @id",
            new { prefix = $"{TillCode(conn, tx)}-{code}-", id }, tx);
    }
}
