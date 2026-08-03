namespace Pos.Core;

/// <summary>
/// The app's own identity — chiefly its version, kept in one place so the footer, the update
/// check and anything else that reports "which build is this" can never disagree.
///
/// Bump <see cref="Version"/> when cutting a release, and match it in the build's manifest on the
/// server (see the app-version endpoint). The auto-updater compares the two: a server version
/// higher than this one means an update is waiting.
/// </summary>
public static class AppInfo
{
    /// <summary>The running build's version. Plain "major.minor.patch".</summary>
    public const string Version = "3.0.0";

    /// <summary>What the footer shows.</summary>
    public static string DisplayVersion => "v" + Version;

    /// <summary>
    /// Compares two "major.minor.patch" versions. Positive when <paramref name="a"/> is newer than
    /// <paramref name="b"/>, negative when older, zero when the same. Missing or non-numeric parts
    /// count as 0, so "3.0" and "3.0.0" are equal and a malformed value never looks newer.
    /// </summary>
    public static int Compare(string? a, string? b)
    {
        var pa = Parse(a);
        var pb = Parse(b);
        for (var i = 0; i < 3; i++)
        {
            var d = pa[i].CompareTo(pb[i]);
            if (d != 0)
            {
                return d;
            }
        }
        return 0;
    }

    /// <summary>True when <paramref name="latest"/> is a strictly newer version than what is
    /// running now.</summary>
    public static bool IsNewer(string? latest) => Compare(latest, Version) > 0;

    private static int[] Parse(string? v)
    {
        var parts = new[] { 0, 0, 0 };
        if (string.IsNullOrWhiteSpace(v))
        {
            return parts;
        }

        // Tolerate a leading "v" and any pre-release suffix ("3.0.0-beta" → 3,0,0).
        var cleaned = v.Trim().TrimStart('v', 'V');
        var dash = cleaned.IndexOf('-');
        if (dash >= 0)
        {
            cleaned = cleaned[..dash];
        }

        var bits = cleaned.Split('.');
        for (var i = 0; i < 3 && i < bits.Length; i++)
        {
            parts[i] = int.TryParse(bits[i], out var n) && n > 0 ? n : 0;
        }
        return parts;
    }
}
