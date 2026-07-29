using Dapper;
using Pos.Core.Data;
using Pos.Core.Models;

namespace Pos.Core.Repositories;

/// <summary>
/// Read access to the local menu cache (categories + menu_items).
///
/// The catalog is shared by every business on the counter — Daal Roti and Chay Chaupal sell
/// from one menu and seat guests at one set of tables, which is exactly how the server stores
/// it (menu_items and categories carry no client_id there). The client_id column on the local
/// copies is a leftover from when this till served one brand; nothing reads it, and filtering
/// on it is what would hand the second brand an empty menu.
/// </summary>
public sealed class MenuRepository
{
    private readonly DatabaseService _db;

    public MenuRepository(DatabaseService db)
    {
        _db = db;
        DapperConfig.Init();
    }

    public IReadOnlyList<Category> GetCategories()
    {
        using var conn = _db.OpenConnection();
        return conn.Query<Category>(
            "SELECT * FROM categories WHERE is_active = 1 ORDER BY sort_order, name").AsList();
    }

    public IReadOnlyList<MenuItem> GetMenuItems()
    {
        using var conn = _db.OpenConnection();
        return conn.Query<MenuItem>(
            "SELECT * FROM menu_items WHERE is_available = 1 ORDER BY sort_order, name").AsList();
    }
}
