using Pos.Core.Data;
using Pos.Core.Repositories;

namespace Pos.Core.Sync;

/// <summary>Everything the UI needs to show about the last sync attempt.</summary>
public sealed record SyncStatus(bool Online, int Pending, DateTime? LastSuccessIst, string? LastError);

/// <summary>
/// Runs both halves of the SQLite ↔ MySQL coordination on a timer:
/// pull master data down, push queued bills up.
///
/// Billing never depends on this. If the server is unreachable the till keeps taking orders
/// into SQLite and the queue simply drains later — that is the whole point of the local
/// database. All timestamps involved are IST.
/// </summary>
public sealed class SyncCoordinator : IDisposable
{
    /// <summary>How often the queue is drained.</summary>
    private static readonly TimeSpan PushInterval = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How often master data is refreshed from the server. Used to be 10 minutes — fine for
    /// this till's own edits (which apply to SQLite immediately, before any network call),
    /// but too slow for seeing what a DIFFERENT till or the admin dashboard just changed.
    /// Matches PushInterval so both directions feel the same: a menu edit anywhere shows up
    /// here within about 20 seconds.
    /// </summary>
    private static readonly TimeSpan PullInterval = TimeSpan.FromSeconds(20);

    public const string ApiUrlSettingKey = "pos_api_base_url";
    public const string DefaultApiUrl = "http://127.0.0.1:8123/api";

    private readonly DatabaseService _db;
    private readonly AppSettingsRepository _settings;
    private CancellationTokenSource? _cts;
    private DateTime _lastPullIst = DateTime.MinValue;

    public SyncCoordinator(DatabaseService db, AppSettingsRepository settings)
    {
        _db = db;
        _settings = settings;
    }

    public SyncStatus Status { get; private set; } = new(false, 0, null, null);

    /// <summary>Raised after every pass so the UI can show the current state.</summary>
    public event Action<SyncStatus>? StatusChanged;

    public string ApiUrl
    {
        get
        {
            var saved = _settings.Get(ApiUrlSettingKey);
            return string.IsNullOrWhiteSpace(saved) ? DefaultApiUrl : saved.Trim().TrimEnd('/');
        }
        set => _settings.Set(ApiUrlSettingKey, (value ?? "").Trim().TrimEnd('/'));
    }

    public void Start()
    {
        if (_cts != null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    /// <summary>Runs one pass immediately (the "sync now" button).</summary>
    public Task<SyncStatus> SyncNowAsync(CancellationToken ct = default) => RunPassAsync(force: true, ct);

    /// <summary>
    /// Fire-and-forget nudge used right after a catalog edit or delete is queued, so it goes
    /// out within the second instead of waiting for the next scheduled pass. Best-effort only
    /// — errors are swallowed exactly like the background loop, because the row is already
    /// safe in the queue and the normal retry picks it up if this attempt doesn't land.
    /// </summary>
    public void NudgePush() => _ = Task.Run(async () =>
    {
        try { await RunPassAsync(force: false, CancellationToken.None); }
        catch { /* the scheduled loop will retry */ }
    });

    /// <summary>A client configured with this till's current server URL, for callers (like
    /// catalog creation) that need to talk to the server directly rather than through the
    /// queue.</summary>
    public PosApiClient CreateApiClient() => new(ApiUrl);

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunPassAsync(force: false, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // A sync problem must never take the till down.
            }

            try
            {
                await Task.Delay(PushInterval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<SyncStatus> RunPassAsync(bool force, CancellationToken ct)
    {
        var api = new PosApiClient(ApiUrl);
        var queue = new SyncQueueService(_db, api);

        if (!await api.IsReachableAsync(ct))
        {
            return Publish(new SyncStatus(false, queue.PendingCount(), Status.LastSuccessIst, "Server offline"));
        }

        // Master data first, so a bill pushed straight after refers to items the server knows.
        string? pullError = null;
        if (force || Ist.Now - _lastPullIst >= PullInterval)
        {
            var pull = await new BootstrapSyncService(_db, api).PullAsync(1, ct);
            if (pull.Ok)
            {
                _lastPullIst = Ist.Now;
            }
            else
            {
                // Surfaced, not swallowed: a pull that quietly fails leaves the till running
                // on a stale menu with nothing on screen to say so.
                pullError = "Pull: " + pull.Error;
            }
        }

        var flush = await queue.FlushAsync(ct);
        var ok = flush.Failed == 0;

        return Publish(new SyncStatus(
            Online: true,
            Pending: flush.Remaining,
            LastSuccessIst: ok ? Ist.Now : Status.LastSuccessIst,
            LastError: pullError ?? flush.LastError));
    }

    private SyncStatus Publish(SyncStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
        return status;
    }

    public void Dispose() => Stop();
}
