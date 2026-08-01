using System.Text.Json;
using Dapper;
using Pos.Core.Data;

namespace Pos.Core.Repositories;

/// <summary>
/// Key/value settings, split across two tables by what the setting describes.
///
/// <c>app_settings</c> holds THIS MACHINE: its printer, paper size, window layout, the server
/// address it talks to, the number that last signed in. These do not change when a different
/// business takes over the counter.
///
/// <c>client_settings</c> holds a BUSINESS: printed header, GST number, FSSAI licence, UPI id,
/// shortcuts. Two brands share this till, so these are keyed by client — one shared row would
/// print Daal Roti's GST number on a Chay Chaupal bill.
///
/// Which one a caller wants is not guessable from the key, so it is in the method name:
/// <see cref="Get"/>/<see cref="Set"/> for the machine, <see cref="GetForClient"/> and friends
/// for the business.
/// </summary>
public sealed class AppSettingsRepository
{
    private readonly DatabaseService _db;
    private readonly ClientContext _client;

    public AppSettingsRepository(DatabaseService db, ClientContext client)
    {
        _db = db;
        _client = client;
    }

    // ── Business settings (per client) ───────────────────────────────────────

    public string? GetForClient(string key, long? clientId = null)
    {
        using var conn = _db.OpenConnection();
        return conn.QueryFirstOrDefault<string>(
            "SELECT value_json FROM client_settings WHERE client_id = @clientId AND key = @key LIMIT 1",
            new { clientId = clientId ?? _client.ClientId, key });
    }

    public void SetForClient(string key, string valueJson, long? clientId = null)
    {
        using var conn = _db.OpenConnection();
        conn.Execute(
            @"INSERT INTO client_settings (client_id, key, value_json, updated_at)
              VALUES (@clientId, @key, @valueJson, datetime('now', '+330 minutes'))
              ON CONFLICT(client_id, key) DO UPDATE SET
                value_json = excluded.value_json,
                updated_at = excluded.updated_at",
            new { clientId = clientId ?? _client.ClientId, key, valueJson });
    }

    public T? GetJsonForClient<T>(string key, long? clientId = null)
    {
        var raw = GetForClient(key, clientId);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return default;
        }
        try
        {
            return JsonSerializer.Deserialize<T>(raw);
        }
        catch
        {
            return default;
        }
    }

    public void SetJsonForClient<T>(string key, T value, long? clientId = null)
        => SetForClient(key, JsonSerializer.Serialize(value), clientId);

    // ── Machine settings ─────────────────────────────────────────────────────

    public string? Get(string key)
    {
        using var conn = _db.OpenConnection();
        return conn.QueryFirstOrDefault<string>(
            "SELECT value_json FROM app_settings WHERE key = @key LIMIT 1", new { key });
    }

    public void Set(string key, string valueJson)
    {
        using var conn = _db.OpenConnection();
        conn.Execute(
            @"INSERT INTO app_settings (key, value_json, updated_at)
              VALUES (@key, @valueJson, datetime('now', '+330 minutes'))
              ON CONFLICT(key) DO UPDATE SET
                value_json = excluded.value_json,
                updated_at = datetime('now', '+330 minutes')",
            new { key, valueJson });
    }

    public T? GetJson<T>(string key)
    {
        var raw = Get(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return default;
        }
        try
        {
            return JsonSerializer.Deserialize<T>(raw);
        }
        catch
        {
            return default;
        }
    }

    public void SetJson<T>(string key, T value)
        => Set(key, JsonSerializer.Serialize(value));

    /// <summary>
    /// Saves a setting and queues it for the server. Use this for anything that describes the
    /// BUSINESS — shop name, GST number, UPI details, shortcuts, PIN — so a second counter or
    /// a fresh install picks it up instead of being set up by hand again.
    ///
    /// Use plain <see cref="Set"/> for anything that describes THIS MACHINE (its printer, its
    /// window layout, its till code, the server address): syncing those would hand the next
    /// counter settings that are wrong for it.
    /// </summary>
    public bool SetSynced(string key, string valueJson)
    {
        var clientId = _client.ClientId;

        // Business settings live per client, and the server keys them the same way — sending
        // one without saying whose it is would overwrite the other brand's profile.
        //
        // Written locally as well as to the server, always. The bill header, the GST number and
        // the UPI id are read from here every time a receipt is printed, and a till that has to
        // ask the server for them stops printing the moment the network does.
        SetForClient(key, valueJson, clientId);

        // Straight to the server first. This used to only queue, so the operator pressed Save,
        // saw "Saved ✓", and the change reached MySQL twenty seconds later — or never, if the
        // API was unreachable, with nothing on screen saying so.
        if (PushNow?.Invoke(key, valueJson, clientId) == true)
        {
            return true;
        }

        // Couldn't reach the server. Keep it in the queue so a later pass still gets it there,
        // and let the caller say the save is only local for now.
        using var conn = _db.OpenConnection();
        conn.Execute(
            @"INSERT INTO sync_queue (entity_type, entity_id, operation, payload_json, status)
              VALUES ('setting', @key, 'upsert', @payload, 'pending')",
            new { key, payload = JsonSerializer.Serialize(new { key, value_json = valueJson, client_id = clientId }) });

        SettingQueued?.Invoke();
        return false;
    }

    public bool SetJsonSynced<T>(string key, T value)
        => SetSynced(key, JsonSerializer.Serialize(value));

    /// <summary>
    /// Sends a setting to the server synchronously, answering false when it didn't land.
    ///
    /// A settable hook rather than a constructor dependency because <see cref="SyncCoordinator"/>
    /// already depends on this repository (it keeps the server address here), and taking it the
    /// other way round too would be a cycle. Wired once at startup.
    /// </summary>
    public Func<string, string, long, bool>? PushNow { get; set; }

    /// <summary>
    /// Refreshes the local mirror from the server. Wired at startup alongside
    /// <see cref="PushNow"/>, and for the same reason it isn't a constructor dependency.
    /// </summary>
    public Func<bool>? PullNow { get; set; }

    /// <summary>
    /// Re-reads this business's settings from the server before they are shown, so the Settings
    /// screen displays what is actually in the database rather than a local copy of it.
    ///
    /// Returns false when the server couldn't be reached — the caller carries on with the local
    /// values, which is the whole reason they are kept.
    /// </summary>
    public bool RefreshFromServer() => PullNow?.Invoke() == true;

    /// <summary>
    /// Renames the business in the local clients row.
    ///
    /// The shop's name is held in two places — the profile setting that gets printed on bills,
    /// and the clients row that names the business to everything else (including the prefix on
    /// every order key). Writing only the setting left the second one stale.
    /// </summary>
    public void SetClientName(string name, long? clientId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var id = clientId ?? _client.ClientId;
        using var conn = _db.OpenConnection();
        conn.Execute("UPDATE clients SET name = @name WHERE id = @clientId",
            new { name = name.Trim(), clientId = id });

        // The bill prefix is short for the name, so it goes stale the moment the name changes:
        // a shop renamed from "Daal Roti" to "Bombay Cafe" would keep printing #DR-0007.
        BillPrefix.Refresh(conn, null, id, name);
    }

    /// <summary>Raised after a synced setting is queued, so the coordinator can push it now
    /// rather than at the next scheduled pass.</summary>
    public event Action? SettingQueued;
}
