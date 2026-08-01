using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Pos.Core.Data;

/// <summary>
/// What a business is called — one question, one answer.
///
/// The name had grown four separate homes, and the till read a different one depending on what
/// it was drawing:
///
/// <list type="bullet">
/// <item><c>client_settings['restaurant_profile'].name</c> — what the Settings screen writes,
/// what the bill header prints, and the only copy that is pushed to the server.</item>
/// <item><c>clients.name</c> — what the sidebar falls back to and what the bill-number prefix is
/// abbreviated from.</item>
/// <item><c>app_settings['restaurant_profile'].name</c> — a one-time copy left behind by the
/// migration that moved business settings into client_settings. Nothing writes it any more, so
/// it holds whatever the shop was called on the day that migration ran.</item>
/// <item><c>app_settings['pos_wpf_settings'].StoreName</c> — where this app used to keep the
/// name before it moved to the shared profile key. Nothing writes it any more either.</item>
/// </list>
///
/// Renaming the shop updated the first two and left the last two frozen, which is why the same
/// till could show one name in the sidebar, print a second on the bill and stamp a third onto
/// its order keys. Everything that needs the name now asks here instead.
///
/// The profile wins because it is the copy the operator actually edits and the only one that
/// travels: <c>clients.name</c> is a local mirror, and a pull overwrites it from the server.
/// </summary>
public static class BusinessName
{
    /// <summary>
    /// This business's name, or empty if it has none yet — a till whose profile has never
    /// synced. Callers are expected to handle empty rather than be handed a guess: a default
    /// here would print one shop's name on another shop's bill.
    /// </summary>
    public static string Resolve(SqliteConnection conn, IDbTransaction? tx, long clientId)
    {
        var fromProfile = FromProfile(conn, tx, clientId);
        if (fromProfile.Length > 0)
        {
            return fromProfile;
        }

        return (conn.QueryFirstOrDefault<string>(
            "SELECT name FROM clients WHERE id = @clientId LIMIT 1", new { clientId }, tx) ?? "").Trim();
    }

    /// <summary>
    /// The name as the Settings screen last saved it. Separate from <see cref="Resolve"/> so the
    /// sync can ask "does the profile have an opinion?" without the clients-row fallback
    /// answering on its behalf — during a pull that fallback is the very value being decided.
    /// </summary>
    public static string FromProfile(SqliteConnection conn, IDbTransaction? tx, long clientId)
    {
        var json = conn.QueryFirstOrDefault<string>(
            "SELECT value_json FROM client_settings WHERE client_id = @clientId AND key = 'restaurant_profile' LIMIT 1",
            new { clientId }, tx);

        if (string.IsNullOrWhiteSpace(json))
        {
            return "";
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                ? (n.GetString() ?? "").Trim()
                : "";
        }
        catch (JsonException)
        {
            // A hand-edited profile must not stop a bill from being saved.
            return "";
        }
    }
}
