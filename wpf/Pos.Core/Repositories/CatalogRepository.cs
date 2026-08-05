using System.Text.Json;
using Dapper;
using Pos.Core.Data;
using Pos.Core.Models;
using Pos.Core.Sync;

namespace Pos.Core.Repositories;

/// <summary>
/// CRUD for the POS-managed catalog entities used by the Settings screen
/// (matching the Electron app's Settings → POS Defaults / Menu Items):
/// dining areas, restaurant tables, categories, GST rates and menu items.
///
/// Creating a row is a synchronous, online-only call to the server: SQLite has no way to
/// hand out an id that is guaranteed to match the server's own auto-increment, so a row
/// created offline could later collide with one the server (or another till) assigned the
/// same number independently — two unrelated menu items silently merging into one on the
/// next pull. Requiring the network for CREATE means the local row is always born with the
/// server's real id.
///
/// Editing or deleting an EXISTING row has no such risk (the id already matches both sides),
/// so those stay local-first: written to SQLite immediately, then queued — offline-safe,
/// same as billing.
/// </summary>
public sealed class CatalogRepository
{
    private readonly DatabaseService _db;
    private readonly SyncCoordinator _sync;

    /// <summary>
    /// The catalog belongs to the counter, not to one of the businesses on it — Daal Roti and
    /// Chay Chaupal sell from the same menu and seat guests at the same tables, which is how
    /// the server stores it too (no client_id on menu_items, categories, tables or areas).
    ///
    /// The local copies still carry the column from when this till served a single brand. It
    /// is NOT NULL, so new rows keep writing the original value; nothing reads it back.
    /// </summary>
    private readonly ClientContext _client;

    public CatalogRepository(DatabaseService db, SyncCoordinator sync, ClientContext client)
    {
        _db = db;
        _sync = sync;
        _client = client;
        DapperConfig.Init();
    }

    // ── Dining Areas ─────────────────────────────────────────────────────────
    public IReadOnlyList<DiningArea> GetAreas()
    {
        using var conn = _db.OpenConnection();
        return conn.Query<DiningArea>(
            "SELECT id, client_id, name, sort_order, is_active FROM dining_areas WHERE is_active = 1 ORDER BY sort_order, name").AsList();
    }

    public long SaveArea(DiningArea a)
    {
        using var conn = _db.OpenConnection();
        if (a.Id > 0)
        {
            conn.Execute("UPDATE dining_areas SET name=@Name, sort_order=@SortOrder, updated_at=datetime('now','+330 minutes') WHERE id=@Id", a);
            Enqueue(conn, "area", a.Id, "upsert", new { name = a.Name, sort_order = a.SortOrder });
            _sync.NudgePush();
            return a.Id;
        }

        var id = CreateOnServer("/dining-areas", new { name = a.Name, sort_order = a.SortOrder });
        conn.Execute(
            "INSERT INTO dining_areas (id, client_id, name, sort_order) VALUES (@id, @clientId, @Name, @SortOrder)",
            new { id, clientId = _client.ClientId, a.Name, a.SortOrder });
        return id;
    }

    public void DeleteArea(long id)
    {
        using var conn = _db.OpenConnection();
        conn.Execute("UPDATE dining_areas SET is_active=0 WHERE id=@id", new { id });
        Enqueue(conn, "area", id, "delete", new { id });
        _sync.NudgePush();
    }

    // ── Tables ───────────────────────────────────────────────────────────────
    public IReadOnlyList<TableEdit> GetTables()
    {
        using var conn = _db.OpenConnection();
        return conn.Query<TableEdit>(
            @"SELECT rt.id, rt.client_id, rt.table_number, rt.area_id, da.name AS area_name
              FROM restaurant_tables rt
              LEFT JOIN dining_areas da ON da.id = rt.area_id
              WHERE rt.is_active = 1
              ORDER BY rt.id").AsList();
    }

    public long SaveTable(TableEdit t)
    {
        using var conn = _db.OpenConnection();
        if (t.Id > 0)
        {
            conn.Execute("UPDATE restaurant_tables SET table_number=@TableNumber, area_id=@AreaId, updated_at=datetime('now','+330 minutes') WHERE id=@Id", t);
            Enqueue(conn, "table", t.Id, "upsert", new { table_number = t.TableNumber, area_id = t.AreaId });
            _sync.NudgePush();
            return t.Id;
        }

        var id = CreateOnServer("/tables", new { table_number = t.TableNumber, area_id = t.AreaId });
        conn.Execute(
            @"INSERT INTO restaurant_tables (id, client_id, table_number, area_id, table_status)
              VALUES (@id, @clientId, @TableNumber, @AreaId, 'available')",
            new { id, clientId = _client.ClientId, t.TableNumber, t.AreaId });
        return id;
    }

    public void DeleteTable(long id)
    {
        using var conn = _db.OpenConnection();
        conn.Execute("UPDATE restaurant_tables SET is_active=0 WHERE id=@id", new { id });
        Enqueue(conn, "table", id, "delete", new { id });
        _sync.NudgePush();
    }

    // ── Categories ───────────────────────────────────────────────────────────
    public IReadOnlyList<Category> GetCategories()
    {
        using var conn = _db.OpenConnection();
        return conn.Query<Category>(
            "SELECT * FROM categories WHERE is_active=1 ORDER BY sort_order, name").AsList();
    }

    public long SaveCategory(Category c)
    {
        using var conn = _db.OpenConnection();
        if (c.Id > 0)
        {
            conn.Execute("UPDATE categories SET name=@Name, parent_id=@ParentId, sort_order=@SortOrder, updated_at=datetime('now','+330 minutes') WHERE id=@Id", c);
            Enqueue(conn, "category", c.Id, "upsert", new { name = c.Name, parent_id = c.ParentId, sort_order = c.SortOrder });
            _sync.NudgePush();
            return c.Id;
        }

        var id = CreateOnServer("/categories", new { name = c.Name, parent_id = c.ParentId, sort_order = c.SortOrder });
        conn.Execute(
            "INSERT INTO categories (id, client_id, name, parent_id, sort_order) VALUES (@id, @clientId, @Name, @ParentId, @SortOrder)",
            new { id, clientId = _client.ClientId, c.Name, c.ParentId, c.SortOrder });
        return id;
    }

    public void DeleteCategory(long id)
    {
        using var conn = _db.OpenConnection();
        conn.Execute("UPDATE categories SET is_active=0 WHERE id=@id", new { id });
        Enqueue(conn, "category", id, "delete", new { id });
        _sync.NudgePush();
    }

    // ── GST Rates ────────────────────────────────────────────────────────────
    public IReadOnlyList<GstRate> GetGstRates()
    {
        using var conn = _db.OpenConnection();
        return conn.Query<GstRate>(
            "SELECT id, client_id, name, rate, is_active FROM gst_rates WHERE is_active=1 ORDER BY id").AsList();
    }

    public long SaveGstRate(GstRate g)
    {
        using var conn = _db.OpenConnection();
        if (g.Id > 0)
        {
            conn.Execute("UPDATE gst_rates SET name=@Name, rate=@Rate WHERE id=@Id", g);
            Enqueue(conn, "gst_rate", g.Id, "upsert", new { name = g.Name, rate_percent = g.Rate });
            _sync.NudgePush();
            return g.Id;
        }

        var id = CreateOnServer("/gst-rates", new { name = g.Name, rate_percent = g.Rate });
        conn.Execute(
            "INSERT INTO gst_rates (id, client_id, name, rate) VALUES (@id, @clientId, @Name, @Rate)",
            new { id, clientId = _client.ClientId, g.Name, g.Rate });
        return id;
    }

    public void DeleteGstRate(long id)
    {
        using var conn = _db.OpenConnection();
        conn.Execute("UPDATE gst_rates SET is_active=0 WHERE id=@id", new { id });
        Enqueue(conn, "gst_rate", id, "delete", new { id });
        _sync.NudgePush();
    }

    // ── Menu Items ───────────────────────────────────────────────────────────
    public IReadOnlyList<MenuItem> GetMenuItems()
    {
        using var conn = _db.OpenConnection();
        return conn.Query<MenuItem>(
            "SELECT * FROM menu_items ORDER BY sort_order, name").AsList();
    }

    public long SaveMenuItem(MenuItem m)
    {
        using var conn = _db.OpenConnection();
        // The server has no veg/non-veg toggle of its own — its menu_items.is_veg column is
        // always 1. Kept here so the mapping is still correct the day that changes; today it
        // just means every item round-trips as "veg".
        var isVeg = m.Type != "non-veg" ? 1 : 0;

        if (m.Id > 0)
        {
            conn.Execute(
                @"UPDATE menu_items SET category_id=@CategoryId, sub_category_id=@SubCategoryId, name=@Name, code=@Code, price=@Price,
                    type=@Type, is_available=@IsAvailable, is_parcel=@IsParcel, updated_at=datetime('now','+330 minutes')
                  WHERE id=@Id", m);
            Enqueue(conn, "menu_item", m.Id, "upsert", new
            {
                name = m.Name, category_id = m.CategoryId, sub_category_id = m.SubCategoryId,
                price = m.Price, is_veg = isVeg, is_available = m.IsAvailable, code = m.Code
            });
            _sync.NudgePush();
            return m.Id;
        }

        var id = CreateOnServer("/menu-items", new
        {
            name = m.Name, category_id = m.CategoryId, sub_category_id = m.SubCategoryId,
            price = m.Price, is_veg = isVeg, is_available = m.IsAvailable, code = m.Code
        });
        conn.Execute(
            @"INSERT INTO menu_items (id, client_id, category_id, sub_category_id, name, code, price, type, is_available, is_parcel)
              VALUES (@id, @clientId, @CategoryId, @SubCategoryId, @Name, @Code, @Price, @Type, @IsAvailable, @IsParcel)",
            new { id, clientId = _client.ClientId, m.CategoryId, m.SubCategoryId, m.Name, m.Code, m.Price, m.Type, m.IsAvailable, m.IsParcel });
        return id;
    }

    public void DeleteMenuItem(long id)
    {
        using var conn = _db.OpenConnection();
        conn.Execute("DELETE FROM menu_items WHERE id=@id", new { id });
        Enqueue(conn, "menu_item", id, "delete", new { id });
        _sync.NudgePush();
    }

    // ── Internals ────────────────────────────────────────────────────────────

    /// <summary>
    /// Blocking POST — this is the one place in the till that requires the network, by
    /// design (see the class comment). Runs on the caller's thread (a Settings button click),
    /// which is fine: it is a small, occasional admin action, not something on the billing
    /// path where a hung network could hold up a customer.
    /// </summary>
    private long CreateOnServer(string path, object payload)
    {
        try
        {
            var api = _sync.CreateApiClient();
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var response = api.PostJsonAsync(path, json).GetAwaiter().GetResult();
            if (response is { } root && root.TryGetProperty("data", out var data) &&
                data.TryGetProperty("id", out var idProp) && idProp.TryGetInt64(out var id))
            {
                return id;
            }
            throw new InvalidOperationException("Server ne id nahi bheji.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                "Server se connect nahi ho paya — item baad me try karein.", ex);
        }
    }

    private static void Enqueue(Microsoft.Data.Sqlite.SqliteConnection conn, string entityType, long entityId, string op, object payload)
    {
        conn.Execute(
            @"INSERT INTO sync_queue (entity_type, entity_id, operation, payload_json, status)
              VALUES (@entityType, @entityId, @op, @payload, 'pending')",
            new { entityType, entityId = entityId.ToString(), op, payload = JsonSerializer.Serialize(payload) });
    }
}
