using System.Text.Json;

namespace Pos.Core.Sync;

/// <summary>What a version check found.</summary>
/// <param name="Available">True when the server is offering a newer build than this one.</param>
/// <param name="Current">The version running now.</param>
/// <param name="Latest">The version the server is offering.</param>
/// <param name="Url">Where to download the new build (a zip of the app folder).</param>
/// <param name="Notes">A short "what's new", shown to the operator before they update.</param>
/// <param name="Mandatory">When true the build should be pushed harder (a blocking prompt); a
/// normal update is only a quiet badge in the footer.</param>
public sealed record AppUpdateInfo(
    bool Available, string Current, string Latest, string Url, string Notes, bool Mandatory);

/// <summary>
/// Asks the server whether a newer build is out. The answer is a small manifest at
/// <c>/api/app-version</c> the shop owner edits when they publish a release — version, a download
/// URL and a note — so rolling out an update is a config change, not a code change.
///
/// Read-only and best-effort: a till that can't reach the server just keeps running on the build
/// it has. Nothing here installs anything; it only reports what's available. Applying the update
/// is the app layer's job (it has to touch files and restart), kept out of Core on purpose.
/// </summary>
public sealed class AppUpdateService
{
    private readonly PosApiClient _api;

    public AppUpdateService(PosApiClient api) => _api = api;

    public async Task<AppUpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var root = await _api.GetAsync("/app-version", ct);
            if (root is null)
            {
                return null;
            }

            var data = root.Value.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object
                ? d
                : root.Value;

            var latest = Str(data, "version");
            if (string.IsNullOrWhiteSpace(latest))
            {
                return null;
            }

            return new AppUpdateInfo(
                Available: AppInfo.IsNewer(latest),
                Current: AppInfo.Version,
                Latest: latest.Trim(),
                Url: Str(data, "url"),
                Notes: Str(data, "notes"),
                Mandatory: Bool(data, "mandatory"));
        }
        catch
        {
            // Offline or a malformed manifest — never a reason to disturb the till.
            return null;
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString())
            : "";

    private static bool Bool(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v))
        {
            return false;
        }
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => v.TryGetInt64(out var n) && n != 0,
            JsonValueKind.String => bool.TryParse(v.GetString(), out var b) ? b : v.GetString() == "1",
            _ => false,
        };
    }
}
