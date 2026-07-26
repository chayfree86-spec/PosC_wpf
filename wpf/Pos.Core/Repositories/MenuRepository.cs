using Dapper;
using Pos.Core.Data;
using Pos.Core.Models;

namespace Pos.Core.Repositories;

/// <summary>Read access to the local menu cache (categories + menu_items).</summary>
public sealed class MenuRepository
{
    private readonly DatabaseService _db;

    public MenuRepository(DatabaseService db)
    {
        _db = db;
        DapperConfig.Init();
    }

    public IReadOnlyList<Category> GetCategories(long clientId = 1)
    {
        using var conn = _db.OpenConnection();
        return conn.Query<Category>(
            "SELECT * FROM categories WHERE client_id = @clientId AND is_active = 1 ORDER BY sort_order, name",
            new { clientId }).AsList();
    }

    public IReadOnlyList<MenuItem> GetMenuItems(long clientId = 1)
    {
        using var conn = _db.OpenConnection();
        return conn.Query<MenuItem>(
            "SELECT * FROM menu_items WHERE client_id = @clientId AND is_available = 1 ORDER BY sort_order, name",
            new { clientId }).AsList();
    }
}
