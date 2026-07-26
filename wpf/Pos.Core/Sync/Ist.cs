namespace Pos.Core.Sync;

/// <summary>
/// India Standard Time, the only clock this system uses.
///
/// Every timestamp — in SQLite, in what we send the server, in logs — is IST. SQLite does it
/// with <c>datetime('now', '+330 minutes')</c>; this is the C# side of the same rule. The
/// machine's own time zone is deliberately ignored so a till set to the wrong zone still
/// files its bills under the right business day.
/// </summary>
public static class Ist
{
    public const int OffsetMinutes = 330;   // UTC+05:30

    public static DateTime Now => DateTime.UtcNow.AddMinutes(OffsetMinutes);

    /// <summary>"yyyy-MM-dd HH:mm:ss" — the shape stored in SQLite and MySQL alike.</summary>
    public static string Stamp() => Now.ToString("yyyy-MM-dd HH:mm:ss");

    public static string Today() => Now.ToString("yyyy-MM-dd");

    /// <summary>
    /// Reads a timestamp that may be UTC/ISO ("2026-07-26T09:15:00Z") and returns it as an
    /// IST "yyyy-MM-dd HH:mm:ss" string. Values already in that plain shape are left alone —
    /// re-shifting them would push every bill 5½ hours into the future.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!value.Contains('T') && !value.Contains('Z'))
        {
            return value;
        }

        return DateTime.TryParse(value, null,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var utc)
            ? utc.AddMinutes(OffsetMinutes).ToString("yyyy-MM-dd HH:mm:ss")
            : value;
    }
}
