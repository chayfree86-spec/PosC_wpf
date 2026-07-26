using System.Text.Json;
using Dapper;
using Pos.Core.Data;

namespace Pos.Core.Repositories;

/// <summary>
/// Key/value settings store backed by the existing <c>app_settings</c> table
/// (key TEXT PRIMARY KEY, value_json TEXT). Used for store profile, printer
/// preferences, etc. Mirrors how the Electron app persisted app settings.
/// </summary>
public sealed class AppSettingsRepository
{
    private readonly DatabaseService _db;

    public AppSettingsRepository(DatabaseService db) => _db = db;

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
    public void SetSynced(string key, string valueJson)
    {
        Set(key, valueJson);

        using var conn = _db.OpenConnection();
        conn.Execute(
            @"INSERT INTO sync_queue (entity_type, entity_id, operation, payload_json, status)
              VALUES ('setting', @key, 'upsert', @payload, 'pending')",
            new { key, payload = JsonSerializer.Serialize(new { key, value_json = valueJson }) });

        SettingQueued?.Invoke();
    }

    public void SetJsonSynced<T>(string key, T value)
        => SetSynced(key, JsonSerializer.Serialize(value));

    /// <summary>
    /// Renames the business in the local clients row.
    ///
    /// The shop's name is held in two places — the profile setting that gets printed on bills,
    /// and the clients row that names the business to everything else (including the prefix on
    /// every order key). Writing only the setting left the second one stale.
    /// </summary>
    public void SetClientName(string name, long clientId = 1)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        using var conn = _db.OpenConnection();
        conn.Execute("UPDATE clients SET name = @name WHERE id = @clientId",
            new { name = name.Trim(), clientId });
    }

    /// <summary>Raised after a synced setting is queued, so the coordinator can push it now
    /// rather than at the next scheduled pass.</summary>
    public event Action? SettingQueued;
}
