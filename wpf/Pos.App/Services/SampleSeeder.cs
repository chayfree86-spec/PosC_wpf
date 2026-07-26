using Dapper;
using Pos.Core.Data;

namespace Pos.App.Services;

/// <summary>
/// Seeds demo data (a client, tables, categories, menu items) the first time
/// the app runs against an empty DB — just so the UI has content to show before
/// the API bootstrap-sync (Phase 7) is wired up. No-op once menu_items exist.
/// </summary>
public static class SampleSeeder
{
    public static void SeedIfEmpty(DatabaseService db)
    {
        using var conn = db.OpenConnection();
        var count = conn.ExecuteScalar<long>("SELECT COUNT(*) FROM menu_items");
        if (count > 0)
        {
            return;
        }

        using var tx = conn.BeginTransaction();

        conn.Execute("INSERT OR IGNORE INTO clients (id, slug, name) VALUES (1, 'chaychaupal', 'Chay Chaupal')", transaction: tx);

        for (var i = 1; i <= 12; i++)
        {
            conn.Execute("INSERT INTO restaurant_tables (id, client_id, table_number) VALUES (@id, 1, @n)",
                new { id = i, n = i.ToString() }, tx);
        }

        var cats = new (long Id, string Name)[]
        {
            (1, "Chai & Coffee"), (2, "Snacks"), (3, "Chaat"), (4, "Beverages"),
        };
        foreach (var (id, name) in cats)
        {
            conn.Execute("INSERT INTO categories (id, client_id, name, sort_order) VALUES (@id, 1, @name, @id)",
                new { id, name }, tx);
        }

        var items = new (long Cat, string Name, double Price, string Type)[]
        {
            (1, "Masala Chai", 15, "veg"),
            (1, "Adrak Chai", 15, "veg"),
            (1, "Kulhad Chai", 20, "veg"),
            (1, "Filter Coffee", 30, "veg"),
            (1, "Cold Coffee", 60, "veg"),
            (2, "Samosa", 20, "veg"),
            (2, "Kachori", 25, "veg"),
            (2, "Bread Pakora", 30, "veg"),
            (2, "Veg Sandwich", 50, "veg"),
            (2, "Maggi", 40, "veg"),
            (3, "Pani Puri", 40, "veg"),
            (3, "Dahi Bhalla", 50, "veg"),
            (3, "Aloo Tikki", 45, "veg"),
            (3, "Papdi Chaat", 55, "veg"),
            (4, "Masala Soda", 30, "veg"),
            (4, "Lassi", 45, "veg"),
            (4, "Nimbu Pani", 25, "veg"),
            (4, "Water Bottle", 20, "veg"),
        };
        var itemId = 1;
        foreach (var (cat, name, price, type) in items)
        {
            conn.Execute(
                "INSERT INTO menu_items (id, client_id, category_id, name, price, type, sort_order) VALUES (@id, 1, @cat, @name, @price, @type, @id)",
                new { id = itemId, cat, name, price, type }, tx);
            itemId++;
        }

        tx.Commit();
    }
}
