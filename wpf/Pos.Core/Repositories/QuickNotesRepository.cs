using System;
using System.Collections.Generic;
using Dapper;
using Pos.Core.Data;
using Pos.Core.Models;

namespace Pos.Core.Repositories;

public sealed class QuickNotesRepository
{
    private readonly DatabaseService _db;

    public QuickNotesRepository(DatabaseService db)
    {
        _db = db;
        DapperConfig.Init();
    }

    public List<QuickNote> GetNotes(long clientId = 1)
    {
        using var conn = _db.OpenConnection();
        try
        {
            const string sql = @"
                SELECT id,
                       coalesce(client_id, 1) AS ClientId,
                       coalesce(customer_name, '') AS CustomerName,
                       coalesce(customer_mobile, '') AS CustomerMobile,
                       coalesce(saved_time, '') AS SavedTime,
                       coalesce(target_time, '') AS TargetTime,
                       total_qty AS TotalQty,
                       grand_total AS GrandTotal,
                       coalesce(items_json, '[]') AS ItemsJson,
                       created_at AS CreatedAt
                FROM quick_notes
                WHERE client_id = @clientId
                ORDER BY id DESC;";
            return conn.Query<QuickNote>(sql, new { clientId }).AsList();
        }
        catch
        {
            const string fallbackSql = @"
                SELECT id,
                       coalesce(client_id, 1) AS ClientId,
                       coalesce(customer_name, '') AS CustomerName,
                       coalesce(customer_mobile, '') AS CustomerMobile,
                       coalesce(saved_time, '') AS SavedTime,
                       '' AS TargetTime,
                       total_qty AS TotalQty,
                       grand_total AS GrandTotal,
                       coalesce(items_json, '[]') AS ItemsJson,
                       created_at AS CreatedAt
                FROM quick_notes
                WHERE client_id = @clientId
                ORDER BY id DESC;";
            return conn.Query<QuickNote>(fallbackSql, new { clientId }).AsList();
        }
    }

    public long SaveNote(QuickNote note)
    {
        using var conn = _db.OpenConnection();
        if (note.Id > 0)
        {
            const string updateSql = @"
                UPDATE quick_notes
                SET customer_name = @CustomerName,
                    customer_mobile = @CustomerMobile,
                    saved_time = @SavedTime,
                    target_time = @TargetTime,
                    total_qty = @TotalQty,
                    grand_total = @GrandTotal,
                    items_json = @ItemsJson
                WHERE id = @Id AND client_id = @ClientId;";
            conn.Execute(updateSql, note);
            return note.Id;
        }
        else
        {
            const string insertSql = @"
                INSERT INTO quick_notes (client_id, customer_name, customer_mobile, saved_time, target_time, total_qty, grand_total, items_json)
                VALUES (@ClientId, @CustomerName, @CustomerMobile, @SavedTime, @TargetTime, @TotalQty, @GrandTotal, @ItemsJson);
                SELECT last_insert_rowid();";
            return conn.ExecuteScalar<long>(insertSql, note);
        }
    }

    public bool DeleteNote(long id, long clientId = 1)
    {
        using var conn = _db.OpenConnection();
        const string sql = "DELETE FROM quick_notes WHERE id = @id AND client_id = @clientId;";
        return conn.Execute(sql, new { id, clientId }) > 0;
    }
}
