using Microsoft.Data.Sqlite;

namespace Pos.Core.Data;

/// <summary>
/// Owns the local SQLite database connection. Ports the behaviour of the
/// Electron app's <c>src/storage/database-service.js</c>:
///  - same pragmas (WAL, foreign_keys, synchronous NORMAL, busy_timeout),
///  - registers the custom <c>uuid()</c> SQL function that the schema's
///    column defaults depend on (e.g. <c>DEFAULT (uuid())</c>).
/// The database file lives at Documents\ChayChaupalPOS\sqlite\pos.sqlite3 by
/// default (a fresh, empty DB is created on first run by the migration runner).
/// </summary>
public sealed class DatabaseService
{
    private readonly string _connectionString;

    public string DbPath { get; }

    public DatabaseService(string dbPath)
    {
        DbPath = dbPath;
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Pooling keeps the WAL file warm and avoids reopen cost.
            Pooling = true,
        }.ToString();
    }

    /// <summary>Default DB path under the user's Documents folder.</summary>
    public static string DefaultDbPath()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(documents))
        {
            documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        var filename = DotEnv.Get("DB_SQLITE_FILE") ?? "pos_wpf.sqlite3";
        return Path.Combine(documents, "ChayChaupalPOS", "sqlite", filename);
    }

    /// <summary>
    /// Opens a new connection, applies the connection-scoped pragmas and
    /// registers the <c>uuid()</c> function. The function must be registered on
    /// every physical connection, so we do it on each open (cheap, idempotent).
    /// Caller owns the returned connection and must dispose it.
    /// </summary>
    public SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();

        conn.CreateFunction("uuid", () => Guid.NewGuid().ToString());

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "PRAGMA journal_mode=WAL;" +
            "PRAGMA foreign_keys=ON;" +
            "PRAGMA synchronous=NORMAL;" +
            // Long enough to ride out a backup, an antivirus scan or someone poking at the
            // file with a DB browser, all of which take a write lock for a moment. A bill
            // must not fail because something else touched the database for two seconds.
            "PRAGMA busy_timeout=15000;" +
            "PRAGMA temp_store=MEMORY;";
        cmd.ExecuteNonQuery();

        return conn;
    }
}
