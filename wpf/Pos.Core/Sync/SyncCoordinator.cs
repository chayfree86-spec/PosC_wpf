using Dapper;
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

    /// <summary>The .env key a deployment sets to point this till at its server.</summary>
    public const string ApiUrlEnvKey = "POS_API_URL";

    private readonly DatabaseService _db;
    private readonly AppSettingsRepository _settings;
    private readonly ClientContext _client;
    private CancellationTokenSource? _cts;
    private DateTime _lastPullIst = DateTime.MinValue;

    public SyncCoordinator(DatabaseService db, AppSettingsRepository settings, ClientContext client)
    {
        _client = client;
        _db = db;
        _settings = settings;
    }

    public SyncStatus Status { get; private set; } = new(false, 0, null, null);

    /// <summary>Raised after every pass so the UI can show the current state.</summary>
    public event Action<SyncStatus>? StatusChanged;

    /// <summary>
    /// The server this till talks to.
    ///
    /// The <c>.env</c> beside the executable wins: that is the file a deployment edits, and the
    /// whole point of moving the address there is that pointing a till at the live server is a
    /// config change rather than a rebuild. Below it sits the value stored in app_settings — a
    /// per-machine override kept for installs that already had one — and finally the built-in
    /// development default.
    /// </summary>
    public string ApiUrl
    {
        get
        {
            if (DotEnv.Get(ApiUrlEnvKey) is { } fromEnv && fromEnv.Trim().Length > 0)
            {
                return fromEnv.Trim().TrimEnd('/');
            }

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
        try { await RunPassAsync(force: true, CancellationToken.None); }
        catch { /* the scheduled loop will retry */ }
    });

    /// <summary>A client configured with this till's current server URL, for callers (like
    /// catalog creation) that need to talk to the server directly rather than through the
    /// queue.</summary>
    public PosApiClient CreateApiClient() => new(ApiUrl, _client.Slug, _client.ClientId);

    /// <summary>
    /// Pulls every business's staff list down from the server before the login screen opens, so a
    /// PIN or user changed on the server is in effect the moment the till next starts — not one
    /// background pass later, after the manager has already been turned away with their old PIN.
    ///
    /// Sign-in is checked against the local copy on purpose (the counter must open when the line
    /// is down), so this is best-effort and time-boxed: if the server is slow or unreachable it
    /// gives up within <paramref name="budget"/> and login falls back to whatever is already in
    /// SQLite. The whole point is only to make that local copy fresh first when the network allows.
    ///
    /// Login is not tied to one business — any brand's manager can sign in — so it refreshes each
    /// client the till already knows about, not just one.
    /// </summary>
    public void RefreshStaffBeforeLogin(TimeSpan budget)
    {
        try
        {
            // Runs on the thread pool, waited (not awaited) with a cap: startup blocks briefly at
            // most, and a call still in flight when the budget runs out simply finishes in the
            // background — its rows are just as welcome a second late.
            Task.Run(RefreshAllClientsAsync).Wait(budget);
        }
        catch
        {
            // Offline, slow, or the task faulted — the local staff list stands and login proceeds.
        }
    }

    private async Task RefreshAllClientsAsync()
    {
        var api = ShortApiClient();
        if (!await api.IsReachableAsync())
        {
            return;
        }

        long[] clientIds;
        using (var conn = _db.OpenConnection())
        {
            clientIds = conn.Query<long>("SELECT id FROM clients").ToArray();
        }

        var boot = new BootstrapSyncService(_db, api);
        foreach (var id in clientIds)
        {
            await boot.PullAsync(id);
        }
    }

    /// <summary>
    /// Writes one setting to the server there and then, and says whether it landed.
    ///
    /// Settings do not go through the queue by default any more. A shop's name, GST number or
    /// UPI id is changed by a manager standing in front of the screen, and the whole point of
    /// pressing Save is to see it take effect — a queued write that quietly waits, or quietly
    /// never goes because the server is down, looks exactly like a save that worked. Bills stay
    /// queued, because a sale must never be refused for want of a network.
    ///
    /// False means the value is saved locally but not on the server; the caller queues it so a
    /// later pass still gets it there, and tells the operator rather than showing a plain
    /// "Saved ✓".
    /// </summary>
    /// <param name="clientId">The business the setting belongs to, recorded at save time.</param>
    public bool PushSettingNow(string key, string valueJson, long clientId)
    {
        if (!DirectWorthTrying)
        {
            return false;
        }

        try
        {
            // Task.Run, not a bare .Result: this is called from the UI thread, and awaiting a
            // continuation that wants that same thread back would deadlock the till on Save.
            // A short timeout for the same reason — the button must not hang on a dead server.
            Task.Run(() => SyncQueueService.PushSettingAsync(ShortApiClient(), key, valueJson, clientId))
                .GetAwaiter().GetResult();
            _directDeadUntil = DateTime.MinValue;
            return true;
        }
        catch
        {
            // Offline, or the server rejected it. Either way the queue is the safety net.
            MarkDirectDead();
            return false;
        }
    }

    /// <summary>
    /// Reads this business's settings back from the server into the local mirror, and says
    /// whether it got through.
    ///
    /// The other half of <see cref="PushSettingNow"/>: saves go straight to the server, so the
    /// Settings screen opens against the server too. Otherwise the operator could be editing a
    /// local copy that no longer matches the row their change is about to overwrite.
    ///
    /// Merged newest-wins, not blindly overwritten — a change saved while the server was
    /// unreachable is still sitting in the queue, and taking the server's older copy here would
    /// throw it away before it was ever sent.
    /// </summary>
    public bool RefreshSettingsNow()
    {
        if (!DirectWorthTrying)
        {
            return false;
        }

        try
        {
            var ok = Task.Run(() => new BootstrapSyncService(_db, ShortApiClient())
                .RefreshSettingsAsync(_client.ClientId)).GetAwaiter().GetResult();
            if (ok)
            {
                _directDeadUntil = DateTime.MinValue;
            }
            else
            {
                MarkDirectDead();
            }
            return ok;
        }
        catch
        {
            MarkDirectDead();
            return false;
        }
    }

    /// <summary>
    /// Pulls the customer ledger from the server there and then — what Len-Den calls as it opens so
    /// the khaata list is the server's current one, not whatever this till last saw. Best-effort;
    /// false just means the screen falls back to the local copy.
    /// </summary>
    public bool RefreshLedgerNow()
    {
        if (!DirectWorthTrying)
        {
            return false;
        }

        try
        {
            var ok = Task.Run(() => new LedgerSyncService(_db).PullAsync(ShortApiClient(), _client.ClientId))
                .GetAwaiter().GetResult();
            if (ok)
            {
                _directDeadUntil = DateTime.MinValue;
            }
            return ok;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The till's API client with a short fuse, for the calls made straight from the UI
    /// thread. The background loop keeps the longer default.</summary>
    private PosApiClient ShortApiClient() =>
        new(ApiUrl, _client.Slug, _client.ClientId, TimeSpan.FromSeconds(5));

    /// <summary>
    /// How long the direct calls stop trying after one of them fails.
    ///
    /// Without this a save on an offline till pays the timeout once per setting, and Save writes
    /// four of them — the counter would freeze for twenty seconds before saying "server offline".
    /// One failure is enough to know; the rest of that save gives up instantly and goes to the
    /// queue, and the background loop keeps retrying on its own schedule regardless.
    /// </summary>
    private static readonly TimeSpan DirectRetryDelay = TimeSpan.FromSeconds(15);

    private DateTime _directDeadUntil = DateTime.MinValue;

    private bool DirectWorthTrying => DateTime.UtcNow >= _directDeadUntil;

    private void MarkDirectDead() => _directDeadUntil = DateTime.UtcNow.Add(DirectRetryDelay);

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
        // Named, not anonymous: without the client headers the server falls back to whichever
        // client is first in its table, so a second business's pushes were landing on the
        // first one's rows.
        var api = new PosApiClient(ApiUrl, _client.Slug, _client.ClientId);
        var queue = new SyncQueueService(_db, api);

        if (!await api.IsReachableAsync(ct))
        {
            MarkDirectDead();
            return Publish(new SyncStatus(false, queue.PendingCount(), Status.LastSuccessIst, "Server offline"));
        }

        // The server is answering again, so the Settings screen should go straight to it on the
        // next save rather than sitting out the rest of the back-off.
        _directDeadUntil = DateTime.MinValue;

        // Master data first, so a bill pushed straight after refers to items the server knows.
        string? pullError = null;
        if (force || Ist.Now - _lastPullIst >= PullInterval)
        {
            // The signed-in business, not a literal: the staff list, the settings and the
            // orders that come down all belong to one client, and pulling client 1's while
            // Chay Chaupal is billing would quietly hand this counter the other brand's data.
            var pull = await new BootstrapSyncService(_db, api).PullAsync(_client.ClientId, ct);
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

            // The customer ledger comes down on the same cadence, so Len-Den stays in step with
            // khaata changes made on the dashboard or another till. Best-effort — a ledger that
            // can't be reached just isn't refreshed this pass.
            await new LedgerSyncService(_db).PullAsync(api, _client.ClientId, ct);
        }

        var flush = await queue.FlushAsync(force, ct);
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
