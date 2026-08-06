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


    /// <summary>Which business the till is billing for; every client-scoped read and write
    /// below defaults to it so no call site has to remember to pass one.</summary>
    private readonly ClientContext _client;

    public TableRepository(DatabaseService db, ClientContext client)
    {
        _db = db;
        _client = client;
        DapperConfig.Init();
    }

    public IReadOnlyList<TableView> All(long? clientId = null)
    {
        clientId ??= _client.ClientId;
        using var conn = _db.OpenConnection();
        return conn.Query<TableView>(
            @"WITH raw_states AS (
                  SELECT rt.id, rt.client_id, rt.table_number, rt.area_id,
                         COALESCE(ts.table_status, rt.table_status, 'available') AS raw_status,
                         COALESCE(ts.current_amount, 0) AS raw_amount,
                         ts.order_timestamp AS raw_timestamp,
                         da.name AS area_name
                  FROM restaurant_tables rt
                  LEFT JOIN table_client_states ts ON ts.table_id = rt.id AND ts.client_id = @clientId
                  LEFT JOIN dining_areas da ON da.id = rt.area_id
                  WHERE rt.is_active = 1
              )
              SELECT id, client_id, table_number, area_id,
                     CASE WHEN raw_status != 'available'
                           AND NOT EXISTS (
                             SELECT 1 FROM orders o
                             WHERE o.table_id = raw_states.id
                               AND o.client_id = @clientId
                               AND o.order_status NOT IN ('cancelled', 'settled')
                           )
                          THEN 'available'
                          ELSE raw_status
                     END AS status,
                     CAST(CASE WHEN raw_status != 'available'
                           AND NOT EXISTS (
                             SELECT 1 FROM orders o
                             WHERE o.table_id = raw_states.id
                               AND o.client_id = @clientId
                               AND o.order_status NOT IN ('cancelled', 'settled')
                           )
                          THEN 0
                          ELSE CASE WHEN raw_status = 'available' THEN 0 ELSE raw_amount END
                     END AS REAL) AS amount,
                     CASE WHEN raw_status != 'available'
                           AND NOT EXISTS (
                             SELECT 1 FROM orders o
                             WHERE o.table_id = raw_states.id
                               AND o.client_id = @clientId
                               AND o.order_status NOT IN ('cancelled', 'settled')
                           )
                          THEN NULL
                          ELSE CASE WHEN raw_status = 'available' THEN NULL ELSE raw_timestamp END
                     END AS order_timestamp,
                     area_name
              FROM raw_states
              ORDER BY id",
            new { clientId }).AsList();
    }

    public void UpdateState(long tableId, string status, double amount = 0, long? timestamp = null, long? clientId = null)
    {
        clientId ??= _client.ClientId;
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
