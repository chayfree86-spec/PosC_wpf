using System.Text.Json;
using Dapper;
using Pos.Core.Data;

namespace Pos.Core.Sync;

public sealed record BootstrapResult(bool Ok, int Categories, int MenuItems, int Tables, int Areas, string? Error);

/// <summary>
/// Pulls the master data (menu, categories, tables, areas, GST, settings) from the server
/// into local SQLite — the inbound half of the SQLite ↔ MySQL coordination. Port of
/// <c>src/storage/bootstrap-sync-service.js</c>.
///
/// Everything is upserted by id, so a pull never destroys local rows the server hasn't heard
/// of; and every timestamp written here is IST.
/// </summary>
public sealed class BootstrapSyncService
{
    private readonly DatabaseService _db;
    private readonly PosApiClient _api;

    public BootstrapSyncService(DatabaseService db, PosApiClient api)
    {
        _db = db;
        _api = api;
        DapperConfig.Init();
    }

    public async Task<BootstrapResult> PullAsync(long clientId = 1, CancellationToken ct = default)
    {
        try
        {
            var root = await _api.GetAsync($"/bootstrap?client_id={clientId}", ct);
            if (root is null)
            {
                return new BootstrapResult(false, 0, 0, 0, 0, "Empty response");
            }

            // The API answers { success, data: {...} }; some deployments return the data flat.
            var data = root.Value.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object
                ? d : root.Value;

            var categories = Array(data, "categories");
            var menuItems = Array(data, "menu_items");
            var tables = Array(data, "tables");
            var areas = Array(data, "dining_areas", "areas");
            var gst = Array(data, "gst_rates");

            using var conn = _db.OpenConnection();
            using var tx = conn.BeginTransaction();

            UpsertAreas(conn, tx, areas, clientId);
            UpsertCategories(conn, tx, categories, clientId);
            UpsertMenuItems(conn, tx, menuItems, clientId);
            UpsertTables(conn, tx, tables, clientId);
            UpsertGstRates(conn, tx, gst, clientId);
            UpsertSettings(conn, tx, Array(data, "settings"));
            UpsertUsers(conn, tx, Array(data, "users"), clientId);
            UpsertClient(conn, tx, data);

            tx.Commit();

            return new BootstrapResult(true, categories.Count, menuItems.Count, tables.Count, areas.Count, null);
        }
        catch (Exception ex)
        {
            return new BootstrapResult(false, 0, 0, 0, 0, ex.Message);
        }
    }

    // ── upserts ──────────────────────────────────────────────────────────────

    private static void UpsertAreas(Microsoft.Data.Sqlite.SqliteConnection conn, System.Data.IDbTransaction tx,
        List<JsonElement> rows, long clientId)
    {
        foreach (var r in rows)
        {
            conn.Execute(
                @"INSERT INTO dining_areas (id, uuid, client_id, name, sort_order, is_active, created_at, updated_at)
                  VALUES (@id, @uuid, @clientId, @name, @sortOrder, @isActive,
                          COALESCE(@createdAt, datetime('now', '+330 minutes')),
                          COALESCE(@updatedAt, datetime('now', '+330 minutes')))
                  ON CONFLICT(id) DO UPDATE SET
                    name = excluded.name, sort_order = excluded.sort_order,
                    is_active = excluded.is_active,
                    updated_at = COALESCE(excluded.updated_at, datetime('now', '+330 minutes'))",
                new
                {
                    id = Num(r, "id"), uuid = Text(r, "uuid"), clientId,
                    name = Text(r, "name"), sortOrder = Num(r, "sort_order"), isActive = Flag(r, "is_active", 1),
                    createdAt = Ist.Normalize(Str(r, "created_at")), updatedAt = Ist.Normalize(Str(r, "updated_at"))
                }, tx);
        }
    }

    private static void UpsertCategories(Microsoft.Data.Sqlite.SqliteConnection conn, System.Data.IDbTransaction tx,
        List<JsonElement> rows, long clientId)
    {
        foreach (var r in rows)
        {
            conn.Execute(
                @"INSERT INTO categories (id, uuid, client_id, name, image, parent_id, sort_order, is_active, sync_version, created_at, updated_at)
                  VALUES (@id, @uuid, @clientId, @name, @image, @parentId, @sortOrder, @isActive, @syncVersion,
                          COALESCE(@createdAt, datetime('now', '+330 minutes')),
                          COALESCE(@updatedAt, datetime('now', '+330 minutes')))
                  ON CONFLICT(id) DO UPDATE SET
                    uuid = excluded.uuid, name = excluded.name, image = excluded.image,
                    parent_id = excluded.parent_id, sort_order = excluded.sort_order,
                    is_active = excluded.is_active, sync_version = excluded.sync_version,
                    updated_at = COALESCE(excluded.updated_at, datetime('now', '+330 minutes'))",
                new
                {
                    id = Num(r, "id"), uuid = Text(r, "uuid"), clientId, name = Text(r, "name"),
                    image = Str(r, "image"), parentId = NumOrNull(r, "parent_id"),
                    sortOrder = Num(r, "sort_order"), isActive = Flag(r, "is_active", 1),
                    syncVersion = Num(r, "sync_version"),
                    createdAt = Ist.Normalize(Str(r, "created_at")), updatedAt = Ist.Normalize(Str(r, "updated_at"))
                }, tx);
        }
    }

    private static void UpsertMenuItems(Microsoft.Data.Sqlite.SqliteConnection conn, System.Data.IDbTransaction tx,
        List<JsonElement> rows, long clientId)
    {
        foreach (var r in rows)
        {
            conn.Execute(
                @"INSERT INTO menu_items (id, uuid, client_id, category_id, sub_category_id, name, code, price,
                                          description, image, type, is_available, is_parcel, sort_order, sync_version,
                                          created_at, updated_at)
                  VALUES (@id, @uuid, @clientId, @categoryId, @subCategoryId, @name, @code, @price,
                          @description, @image, @type, @isAvailable, @isParcel, @sortOrder, @syncVersion,
                          COALESCE(@createdAt, datetime('now', '+330 minutes')),
                          COALESCE(@updatedAt, datetime('now', '+330 minutes')))
                  ON CONFLICT(id) DO UPDATE SET
                    uuid = excluded.uuid, category_id = excluded.category_id,
                    sub_category_id = excluded.sub_category_id, name = excluded.name, code = excluded.code,
                    price = excluded.price, description = excluded.description, image = excluded.image,
                    type = excluded.type, is_available = excluded.is_available,
                    sync_version = excluded.sync_version,
                    updated_at = COALESCE(excluded.updated_at, datetime('now', '+330 minutes'))",
                new
                {
                    id = Num(r, "id"), uuid = Text(r, "uuid"), clientId,
                    categoryId = NumOrNull(r, "category_id"), subCategoryId = NumOrNull(r, "sub_category_id"),
                    name = Text(r, "name"), code = Str(r, "code"), price = Dbl(r, "price"),
                    description = Str(r, "description"), image = Str(r, "image"),
                    // The server's menu_items table has no "type" column — it stores veg/non-veg
                    // as the boolean is_veg. Reading a "type" key here (the old code did) always
                    // missed and silently fell back to "veg" for every item, on every pull.
                    type = Flag(r, "is_veg", 1) == 1 ? "veg" : "non-veg",
                    isAvailable = Flag(r, "is_available", 1),
                    // is_parcel and sort_order don't exist on the server at all — they're this
                    // till's own menu customization. Left out of the UPDATE SET above on
                    // purpose: they're only given a starting value (0) the first time a row is
                    // ever inserted, and never touched again by a later pull. Including them in
                    // the UPDATE used to reset every item to is_parcel=0 / sort_order=0 on every
                    // single pull, silently undoing any local reordering or parcel flag.
                    isParcel = 0, sortOrder = 0,
                    syncVersion = Num(r, "sync_version"),
                    createdAt = Ist.Normalize(Str(r, "created_at")), updatedAt = Ist.Normalize(Str(r, "updated_at"))
                }, tx);
        }
    }

    private static void UpsertTables(Microsoft.Data.Sqlite.SqliteConnection conn, System.Data.IDbTransaction tx,
        List<JsonElement> rows, long clientId)
    {
        foreach (var r in rows)
        {
            // Only the definition is pulled. table_status / current_amount / order_timestamp
            // are this till's own floor state — the server's copy is stale the moment a table
            // is seated here, so pulling them would wipe live tables mid-service.
            conn.Execute(
                @"INSERT INTO restaurant_tables (id, uuid, client_id, table_number, area_id, qr_code, qr_token,
                                                 is_active, sync_version, created_at, updated_at)
                  VALUES (@id, @uuid, @clientId, @tableNumber, @areaId, @qrCode, @qrToken,
                          @isActive, @syncVersion,
                          COALESCE(@createdAt, datetime('now', '+330 minutes')),
                          COALESCE(@updatedAt, datetime('now', '+330 minutes')))
                  ON CONFLICT(id) DO UPDATE SET
                    uuid = excluded.uuid, area_id = excluded.area_id, table_number = excluded.table_number,
                    qr_code = excluded.qr_code, qr_token = excluded.qr_token,
                    is_active = excluded.is_active, sync_version = excluded.sync_version,
                    updated_at = COALESCE(excluded.updated_at, datetime('now', '+330 minutes'))",
                new
                {
                    id = Num(r, "id"), uuid = Text(r, "uuid"), clientId,
                    areaId = NumOrNull(r, "area_id") ?? NumOrNull(r, "dining_area_id"),
                    tableNumber = Text(r, "table_number"),
                    qrCode = Str(r, "qr_code"), qrToken = Str(r, "qr_token"),
                    isActive = Flag(r, "is_active", 1), syncVersion = Num(r, "sync_version"),
                    createdAt = Ist.Normalize(Str(r, "created_at")), updatedAt = Ist.Normalize(Str(r, "updated_at"))
                }, tx);
        }
    }

    private static void UpsertGstRates(Microsoft.Data.Sqlite.SqliteConnection conn, System.Data.IDbTransaction tx,
        List<JsonElement> rows, long clientId)
    {
        foreach (var r in rows)
        {
            // The server calls it rate_percent; locally the column is just rate. And this
            // table has no uuid/updated_at of its own, so neither is written.
            conn.Execute(
                @"INSERT INTO gst_rates (id, client_id, name, rate, is_active, created_at)
                  VALUES (@id, @clientId, @name, @rate, @isActive,
                          COALESCE(@createdAt, datetime('now', '+330 minutes')))
                  ON CONFLICT(id) DO UPDATE SET
                    name = excluded.name, rate = excluded.rate, is_active = excluded.is_active",
                new
                {
                    id = Num(r, "id"), clientId, name = Text(r, "name"),
                    rate = Dbl(r, "rate_percent") is var rp && rp > 0 ? rp : Dbl(r, "rate"),
                    isActive = Flag(r, "is_active", 1),
                    createdAt = Ist.Normalize(Str(r, "created_at"))
                }, tx);
        }
    }

    /// <summary>
    /// The operators, mirrored from the server. Read-only here — staff are managed centrally,
    /// and no credentials come down with them (the server sends name/role/contact only).
    /// </summary>
    private static void UpsertUsers(Microsoft.Data.Sqlite.SqliteConnection conn, System.Data.IDbTransaction tx,
        List<JsonElement> rows, long clientId)
    {
        foreach (var r in rows)
        {
            conn.Execute(
                @"INSERT INTO users (id, uuid, client_id, name, phone, email, role, is_active, pin, updated_at)
                  VALUES (@id, @uuid, @clientId, @name, @phone, @email, @role, @isActive, @pin,
                          COALESCE(@updatedAt, datetime('now', '+330 minutes')))
                  ON CONFLICT(id) DO UPDATE SET
                    uuid = excluded.uuid, name = excluded.name, phone = excluded.phone,
                    email = excluded.email, role = excluded.role, is_active = excluded.is_active,
                    -- Keep the PIN we already hold if this response didn't carry one, so a
                    -- pull from an older server can't lock everyone out of the counter.
                    pin = COALESCE(excluded.pin, users.pin),
                    updated_at = COALESCE(excluded.updated_at, datetime('now', '+330 minutes'))",
                new
                {
                    id = Num(r, "id"), uuid = Str(r, "uuid"), clientId,
                    name = Text(r, "name"), phone = Str(r, "phone"), email = Str(r, "email"),
                    role = Str(r, "role"), isActive = Flag(r, "is_active", 1),
                    pin = Str(r, "pin"),
                    updatedAt = Ist.Normalize(Str(r, "updated_at"))
                }, tx);
        }
    }

    /// <summary>
    /// Keeps the local clients row named the same as the server's.
    ///
    /// It isn't only cosmetic: the business code at the front of every order key falls back to
    /// this name, so a rename made anywhere else has to reach the till.
    /// </summary>
    private static void UpsertClient(Microsoft.Data.Sqlite.SqliteConnection conn, System.Data.IDbTransaction tx,
        JsonElement data)
    {
        if (!data.TryGetProperty("client", out var client) || client.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var id = Num(client, "id");
        var name = Text(client, "name");
        if (id <= 0 || name.Length == 0)
        {
            return;
        }

        conn.Execute(
            @"INSERT INTO clients (id, slug, name, updated_at)
              VALUES (@id, @slug, @name, datetime('now', '+330 minutes'))
              ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                slug = COALESCE(NULLIF(excluded.slug, ''), clients.slug),
                updated_at = datetime('now', '+330 minutes')",
            new { id, slug = Text(client, "slug"), name }, tx);
    }

    /// <summary>
    /// Settings that describe the BUSINESS and are therefore worth pulling. Anything about
    /// this particular machine — its printer, its window layout, its till code, the server
    /// address it talks to — is missing from this list on purpose: pulling those would hand
    /// this counter another counter's hardware.
    /// </summary>
    private static readonly HashSet<string> SyncedSettingKeys = new()
    {
        // Shared with the dashboard, the mobile menu and the Electron app.
        "restaurant_profile",
        "upi_settings",
        "daily_reset_bill_counter",
        "login_pin",
        // This app's own: which of the profile details get printed, and the hotkeys.
        "pos_wpf_settings",
        "pos_wpf_shortcuts",
    };

    /// <summary>
    /// Brings shared settings down, newest wins.
    ///
    /// The timestamp check is what makes this safe to run every 20 seconds: a pull happens
    /// just before each push, so without it a setting saved a second ago would be overwritten
    /// by the server's older copy before it ever got the chance to be sent.
    /// </summary>
    private static void UpsertSettings(Microsoft.Data.Sqlite.SqliteConnection conn, System.Data.IDbTransaction tx,
        List<JsonElement> rows)
    {
        foreach (var r in rows)
        {
            var key = Str(r, "key");
            if (string.IsNullOrWhiteSpace(key) || !SyncedSettingKeys.Contains(key))
            {
                continue;
            }

            var value = r.TryGetProperty("value", out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                ? v.GetRawText()
                : null;
            if (value is null)
            {
                continue;
            }

            conn.Execute(
                @"INSERT INTO app_settings (key, value_json, updated_at)
                  VALUES (@key, @value, COALESCE(@updatedAt, datetime('now', '+330 minutes')))
                  ON CONFLICT(key) DO UPDATE SET
                    value_json = excluded.value_json,
                    updated_at = excluded.updated_at
                  WHERE COALESCE(excluded.updated_at, '') > COALESCE(app_settings.updated_at, '')",
                new { key, value, updatedAt = Ist.Normalize(Str(r, "updated_at")) }, tx);
        }
    }

    // ── JSON helpers: the API answers with mixed string/number types ──────────

    private static List<JsonElement> Array(JsonElement parent, params string[] names)
    {
        foreach (var name in names)
        {
            if (parent.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Array)
            {
                return el.EnumerateArray().ToList();
            }
        }
        return new List<JsonElement>();
    }

    /// <summary>
    /// A NOT NULL column can't take the server's null. An explicit NULL beats the column's
    /// DEFAULT in SQLite, so the fallback has to be supplied here — this is what made the
    /// whole pull fail on menu_items.type.
    /// </summary>
    private static string Text(JsonElement e, string name, string fallback = "") =>
        Str(e, name) is { } v && v.Trim().Length > 0 ? v : fallback;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())
            : null;

    private static long Num(JsonElement e, string name) => NumOrNull(e, name) ?? 0;

    private static long? NumOrNull(JsonElement e, string name)
    {
        var raw = Str(e, name);
        return long.TryParse(raw, out var n) ? n : null;
    }

    private static double Dbl(JsonElement e, string name) =>
        double.TryParse(Str(e, name), out var d) ? d : 0;

    /// <summary>Reads a 0/1 flag. When the server says nothing at all the column's own
    /// default applies — "not mentioned" must not silently mean "off" for things like
    /// is_available, which would hide every item from the menu.</summary>
    private static int Flag(JsonElement e, string name, int fallback = 0)
    {
        var raw = (Str(e, name) ?? "").Trim().ToLowerInvariant();
        if (raw.Length == 0) return fallback;
        return raw is "1" or "true" or "yes" ? 1 : 0;
    }
}
