using Dapper;
using Pos.Core.Data;
using Pos.Core.Models;
using Pos.Core.Sync;

namespace Pos.Core.Repositories;

/// <summary>
/// The udhaar book — customers and their ledger entries.
///
/// Every change is written to SQLite first and then queued for the server, exactly like a
/// bill. This is real money owed, so it must survive the machine dying, and it must keep
/// working when the line is down.
/// </summary>
public sealed class CustomerLedgerRepository
{
    private readonly DatabaseService _db;
    private readonly SyncCoordinator _sync;


    /// <summary>Which business the till is billing for; every client-scoped read and write
    /// below defaults to it so no call site has to remember to pass one.</summary>
    private readonly ClientContext _client;

    public CustomerLedgerRepository(DatabaseService db, SyncCoordinator sync, ClientContext client)
    {
        _db = db;
        _sync = sync;
        _client = client;
    }

    public IEnumerable<Customer> GetCustomers(long? clientId = null)
    {
        clientId ??= _client.ClientId;
        using var conn = _db.OpenConnection();
        // Balance is COMPUTED live from the ledger entries (money you GAVE ='gave'/'debit'
        // increases udhaar +; money you GOT ='got'/'credit'/'payment' reduces it -), so it
        // stays correct even for rows whose stored balance column was never back-filled.
        const string sql = @"
            SELECT c.id, coalesce(c.client_id, 1) AS client_id, coalesce(c.name, 'Customer') AS name,
                   coalesce(c.phone, c.mobile, '') AS phone, coalesce(c.address, '') AS address,
                   COALESCE((
                     SELECT SUM(CASE
                                  WHEN le.type IN ('gave','debit') THEN le.amount
                                  WHEN le.type IN ('got','credit','payment') THEN -le.amount
                                  ELSE 0 END)
                     FROM ledger_entries le WHERE le.customer_id = c.id
                   ), 0) AS balance,
                   c.created_at
            FROM customers c
            WHERE coalesce(c.client_id, 1) = @clientId
            ORDER BY c.name ASC;";
        return conn.Query<Customer>(sql, new { clientId });
    }

    public long SaveCustomer(Customer c)
    {
        using var conn = _db.OpenConnection();
        long id;
        if (c.Id > 0)
        {
            const string sql = @"
                UPDATE customers
                SET name = @Name, phone = @Phone, address = @Address,
                    updated_at = datetime('now', '+330 minutes')
                WHERE id = @Id;";
            conn.Execute(sql, c);
            id = c.Id;
        }
        else
        {
            // mobile as well as phone: the server keys customers on mobile, and the local
            // schema carries both columns from the Electron days.
            const string sql = @"
                INSERT INTO customers (client_id, name, phone, mobile, address, balance, created_at, updated_at)
                VALUES (@ClientId, @Name, @Phone, @Phone, @Address, @Balance, @CreatedAt,
                        datetime('now', '+330 minutes'));
                SELECT last_insert_rowid();";
            id = conn.ExecuteScalar<long>(sql, c);
            ReadableUuid.Stamp(conn, null, "customers", ReadableUuid.Customer, id, c.ClientId);
        }

        Enqueue(conn, "customer", id, "upsert", new { name = c.Name, mobile = c.Phone, address = c.Address });
        _sync.NudgePush();
        return id;
    }

    public IEnumerable<LedgerEntry> GetLedgerEntries(long customerId)
    {
        using var conn = _db.OpenConnection();
        const string sql = @"
            SELECT id, coalesce(client_id, 1) AS client_id, customer_id, type, amount, coalesce(payment_mode, 'cash') AS payment_mode, coalesce(remarks, note, '') AS remarks, created_at
            FROM ledger_entries
            WHERE customer_id = @customerId
            ORDER BY id DESC;";
        return conn.Query<LedgerEntry>(sql, new { customerId });
    }

    public void AddLedgerEntry(LedgerEntry entry)
    {
        using var conn = _db.OpenConnection();
        using var tx = conn.BeginTransaction();

        const string insertSql = @"
            INSERT INTO ledger_entries (client_id, customer_id, type, amount, payment_mode, remarks, note, created_at, updated_at)
            VALUES (@ClientId, @CustomerId, @Type, @Amount, @PaymentMode, @Remarks, @Remarks, @CreatedAt,
                    datetime('now', '+330 minutes'));";
        conn.Execute(insertSql, entry, tx);
        var entryId = conn.ExecuteScalar<long>("SELECT last_insert_rowid()", transaction: tx);
        ReadableUuid.Stamp(conn, tx, "ledger_entries", ReadableUuid.LedgerEntry, entryId, entry.ClientId);

        // Update customer balance: 'gave' increases Udhaar balance (+), 'got' reduces Udhaar balance (-)
        double balanceChange = entry.Type == "gave" ? entry.Amount : -entry.Amount;
        const string updateBalanceSql = @"
            UPDATE customers
            SET balance = balance + @balanceChange
            WHERE id = @CustomerId;";
        conn.Execute(updateBalanceSql, new { balanceChange, entry.CustomerId }, tx);

        Enqueue(conn, "ledger_entry", entryId, "upsert", LedgerBody(entry), tx);
        tx.Commit();
        _sync.NudgePush();
    }

    public void UpdateLedgerEntry(LedgerEntry e)
    {
        using var conn = _db.OpenConnection();
        conn.Execute(
            @"UPDATE ledger_entries
              SET type = @Type, amount = @Amount, payment_mode = @PaymentMode, remarks = @Remarks,
                  note = @Remarks, created_at = @CreatedAt, updated_at = datetime('now', '+330 minutes')
              WHERE id = @Id", e);
        Enqueue(conn, "ledger_entry", e.Id, "upsert", LedgerBody(e));
        _sync.NudgePush();
    }

    public void DeleteLedgerEntry(long id)
    {
        using var conn = _db.OpenConnection();
        conn.Execute("DELETE FROM ledger_entries WHERE id = @id", new { id });
        Enqueue(conn, "ledger_entry", id, "delete", new { id });
        _sync.NudgePush();
    }

    public void DeleteCustomer(long id)
    {
        using var conn = _db.OpenConnection();
        using var tx = conn.BeginTransaction();
        conn.Execute("DELETE FROM ledger_entries WHERE customer_id = @id", new { id }, tx);
        conn.Execute("DELETE FROM customers WHERE id = @id", new { id }, tx);
        Enqueue(conn, "customer", id, "delete", new { id }, tx);
        tx.Commit();
        _sync.NudgePush();
    }

    /// <summary>
    /// The entry as the server wants it. Two shapes have to be reconciled: locally an entry
    /// is 'gave'/'got' (or the older debit/credit/payment), on the server it is strictly
    /// debit/credit — money given out is a debit against the customer.
    /// </summary>
    private static object LedgerBody(LedgerEntry e) => new
    {
        entry_type = e.IsDebit ? "debit" : "credit",
        type = e.IsDebit ? "debit" : "credit",
        amount = e.Amount,
        note = e.Remarks,
        payment_mode = string.IsNullOrWhiteSpace(e.PaymentMode) ? null : e.PaymentMode.ToLowerInvariant(),
        occurred_at = e.CreatedAt,
        customer_id = e.CustomerId
    };

    private static void Enqueue(Microsoft.Data.Sqlite.SqliteConnection conn, string entityType, long entityId,
        string op, object payload, System.Data.IDbTransaction? tx = null)
    {
        conn.Execute(
            @"INSERT INTO sync_queue (entity_type, entity_id, operation, payload_json, status)
              VALUES (@entityType, @entityId, @op, @payload, 'pending')",
            new { entityType, entityId = entityId.ToString(), op,
                  payload = System.Text.Json.JsonSerializer.Serialize(payload) }, tx);
    }
}
