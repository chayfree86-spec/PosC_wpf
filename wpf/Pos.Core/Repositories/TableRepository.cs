using Dapper;
using Pos.Core.Data;
using Pos.Core.Models;

namespace Pos.Core.Repositories;

/// <summary>
/// Port of <c>src/storage/table-repository.js</c>. Returns tables with their
/// effective status (a table shows "available" if its state row says so, or if no
/// live order backs a non-available state) and upserts table_client_states +
/// restaurant_tables together.
/// </summary>
public sealed class TableRepository
{
    private readonly DatabaseService _db;

    public TableRepository(DatabaseService db)
    {
        _db = db;
        DapperConfig.Init();
    }

    public IReadOnlyList<TableView> All(long clientId = 1)
    {
        using var conn = _db.OpenConnection();
        return conn.Query<TableView>(
            @"SELECT rt.id, rt.client_id, rt.table_number, rt.area_id,
                     CASE WHEN COALESCE(ts.table_status, 'available') != 'available'
                           AND NOT EXISTS (
                             SELECT 1 FROM orders o
                             WHERE o.table_id = ts.table_id
                               AND o.client_id = ts.client_id
                               AND o.order_status NOT IN ('cancelled', 'settled')
                           )
                          THEN 'available'
                          ELSE COALESCE(ts.table_status, rt.table_status, 'available')
                     END AS status,
                     CAST(CASE WHEN COALESCE(ts.table_status, 'available') = 'available' THEN 0 ELSE COALESCE(ts.current_amount, 0) END AS REAL) AS amount,
                     CASE WHEN COALESCE(ts.table_status, 'available') = 'available' THEN NULL ELSE ts.order_timestamp END AS order_timestamp,
                     da.name AS area_name
              FROM restaurant_tables rt
              LEFT JOIN table_client_states ts ON ts.table_id = rt.id AND ts.client_id = @clientId
              LEFT JOIN dining_areas da ON da.id = rt.area_id
              WHERE rt.client_id = @clientId OR rt.client_id IS NULL
              ORDER BY rt.id",
            new { clientId }).AsList();
    }

    public void UpdateState(long tableId, string status, double amount = 0, long? timestamp = null, long clientId = 1)
    {
        using var conn = _db.OpenConnection();
        using var tx = conn.BeginTransaction();

        var isAvailable = status == "available";
        conn.Execute(
            @"INSERT INTO table_client_states (client_id, table_id, table_status, current_amount, order_timestamp)
              VALUES (@clientId, @tableId, @status, @amount, @timestamp)
              ON CONFLICT(client_id, table_id) DO UPDATE SET
                table_status = excluded.table_status,
                current_amount = excluded.current_amount,
                order_timestamp = excluded.order_timestamp,
                updated_at = datetime('now', '+330 minutes')",
            new { clientId, tableId, status, amount = isAvailable ? 0 : amount, timestamp = isAvailable ? (long?)null : timestamp }, tx);

        conn.Execute(
            @"UPDATE restaurant_tables
              SET table_status = @status, current_amount = @amount, order_timestamp = @timestamp,
                  updated_at = datetime('now', '+330 minutes')
              WHERE id = @tableId",
            new { status, amount = isAvailable ? 0 : amount, timestamp = isAvailable ? (long?)null : timestamp, tableId }, tx);

        tx.Commit();
    }
}
