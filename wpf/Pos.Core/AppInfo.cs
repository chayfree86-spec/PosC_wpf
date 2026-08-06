namespace Pos.Core;

/// <summary>
/// The app's own identity — its version and where it looks for updates, in one place so the footer
/// and the updater never disagree.
///
/// <see cref="Version"/> is bumped when cutting a release and must match the <c>--packVersion</c>
/// passed to <c>vpk pack</c>. Velopack compares the running build against the feed at
/// <see cref="UpdateFeedUrl"/> (a folder of release files the packer produces) and offers anything
/// newer.
/// </summary>
public static class AppInfo
{
    /// <summary>The running build's version. Plain "major.minor.patch".</summary>
    public const string Version = "3.0.7";

    /// <summary>What the footer shows.</summary>
    public static string DisplayVersion => "v" + Version;

    /// <summary>
    /// Where published releases live — the folder <c>vpk pack</c> fills with the installer, the
    /// RELEASES index and the update packages, uploaded to the server. Publishing an update is:
    /// build, <c>vpk pack</c>, upload this folder's contents here.
    /// </summary>
    public const string UpdateFeedUrl = "https://posapi-v2.chaychaupal.com/downloads/pos/";
}
