using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Pos.Core.Data;

/// <summary>
/// The letters in front of every bill number — #CC-0007, #DR-0007.
///
/// Kept in <c>clients.bill_prefix</c> rather than worked out from the slug at each call site.
/// Both places that needed it matched the slug against a hardcoded list, and they didn't
/// match it the same way: the till derived initials for an unrecognised business while
/// Reports simply returned "DR", so a third shop on the counter had its bills listed under
/// Dal Roti's prefix. One column now answers for both, and adding a business needs no code.
///
/// The stored value is an abbreviation of the business NAME — the Settings screen's Store
/// Name, which <c>AppSettingsRepository.SetClientName</c> mirrors into <c>clients.name</c> —
/// and it is rewritten whenever that name changes, so the prefix always reads as short for
/// the shop whose bill it is.
/// </summary>
public static class BillPrefix
{
    /// <summary>
    /// This client's prefix, filling the column in from the business name the first time it
    /// is asked for so existing installs don't need a data migration.
    ///
    /// Empty when the business has no name yet. That is deliberate: a till that hasn't synced
    /// its profile prints <c>#0007</c> rather than stamping the bill with some other shop's
    /// letters, which is what the old <c>"DR"</c> default did.
    /// </summary>
    public static string Resolve(SqliteConnection conn, IDbTransaction? tx, long clientId)
    {
        var stored = (conn.QueryFirstOrDefault<string>(
            "SELECT bill_prefix FROM clients WHERE id = @clientId LIMIT 1", new { clientId }, tx) ?? "").Trim();
        if (stored.Length > 0)
        {
            return stored;
        }

        // Through BusinessName rather than clients.name directly: the Settings screen writes the
        // profile, and on a till whose clients row is still carrying the server's older name the
        // two disagree. The prefix has to read as short for whatever the bill header prints.
        var derived = FromName(BusinessName.Resolve(conn, tx, clientId));
        if (derived.Length > 0)
        {
            Store(conn, tx, clientId, derived);
        }
        return derived;
    }

    /// <summary>
    /// Initials of the business name — "Chay Chaupal" → CC, "Daal Roti" → DR. A one-word name
    /// gives its first two letters instead ("Barista" → BA), so every prefix is the same shape.
    /// Punctuation is ignored, which is what keeps "Chay-Chaupal" reading the same as "Chay
    /// Chaupal".
    /// </summary>
    public static string FromName(string? name)
    {
        var words = (name ?? "")
            .Split(new[] { ' ', '\t', '-', '_', '.', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()))
            .Where(w => w.Length > 0)
            .ToList();

        if (words.Count == 0)
        {
            return "";
        }

        return (words.Count >= 2
            ? string.Concat(words[0][0], words[1][0])
            : words[0][..Math.Min(2, words[0].Length)]).ToUpperInvariant();
    }

    /// <summary>
    /// Re-derives and saves the prefix for a business whose name has just changed, wherever
    /// the rename came from — the Settings screen or a bootstrap pull. Without this the column
    /// would hold the abbreviation of a name the shop no longer goes by.
    /// </summary>
    public static void Refresh(SqliteConnection conn, IDbTransaction? tx, long clientId, string? name)
    {
        var derived = FromName(name);
        if (derived.Length > 0)
        {
            Store(conn, tx, clientId, derived);
        }
    }

    private static void Store(SqliteConnection conn, IDbTransaction? tx, long clientId, string prefix)
        => conn.Execute("UPDATE clients SET bill_prefix = @prefix WHERE id = @clientId",
            new { prefix, clientId }, tx);

    /// <summary>
    /// A bill number as it appears on screen and on paper: <c>#CC-0007</c>, or <c>#0007</c>
    /// when there is no name to abbreviate — never <c>#-0007</c>.
    /// </summary>
    public static string Format(string? prefix, long billNumber)
    {
        var padded = billNumber.ToString().PadLeft(4, '0');
        return string.IsNullOrWhiteSpace(prefix) ? $"#{padded}" : $"#{prefix.Trim()}-{padded}";
    }
}
