using Microsoft.Data.Sqlite;

namespace Pos.Core.Data;

/// <summary>
/// Faithful C# port of the Electron app's <c>src/storage/migration-manager.js</c>.
/// Applies the same versioned migrations (1..12) so a fresh, empty SQLite file
/// ends up with the identical 20-table schema and the same <c>schema_migrations</c>
/// rows. Each migration runs in its own transaction and is skipped if its version
/// has already been recorded (idempotent).
/// Timestamps use <c>datetime('now', '+330 minutes')</c> to store IST, matching
/// the original.
/// </summary>
public sealed class SqliteMigrationRunner
{
    private readonly DatabaseService _db;

    public SqliteMigrationRunner(DatabaseService db) => _db = db;

    public void Migrate()
    {
        using var conn = _db.OpenConnection();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    applied_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes'))
                );";
            cmd.ExecuteNonQuery();
        }

        using (var tx = conn.BeginTransaction())
        {
            var ctx = new Ctx(conn, tx);
            ctx.AddColumnIfMissing("customers", "address", "TEXT");
            ctx.AddColumnIfMissing("customers", "phone", "TEXT");
            ctx.AddColumnIfMissing("customers", "balance", "REAL NOT NULL DEFAULT 0");
            ctx.AddColumnIfMissing("ledger_entries", "client_id", "INTEGER NOT NULL DEFAULT 1");
            ctx.AddColumnIfMissing("ledger_entries", "payment_mode", "TEXT NOT NULL DEFAULT 'cash'");
            ctx.AddColumnIfMissing("ledger_entries", "remarks", "TEXT");
            tx.Commit();
        }

        Apply(conn, 1, "sqlite_pos_core_schema", m => m.Exec(@"
            CREATE TABLE IF NOT EXISTS clients (
                id INTEGER PRIMARY KEY,
                uuid TEXT NOT NULL DEFAULT (uuid()),
                slug TEXT UNIQUE,
                name TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes')),
                updated_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes'))
            );

            CREATE TABLE IF NOT EXISTS customers (
                id INTEGER PRIMARY KEY,
                uuid TEXT NOT NULL DEFAULT (uuid()),
                client_id INTEGER NOT NULL DEFAULT 1,
                name TEXT,
                mobile TEXT,
                email TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes')),
                updated_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes')),
                UNIQUE(client_id, mobile)
            );

            CREATE TABLE IF NOT EXISTS dining_areas (
                id INTEGER PRIMARY KEY,
                uuid TEXT NOT NULL DEFAULT (uuid()),
                client_id INTEGER NOT NULL DEFAULT 1,
                name TEXT NOT NULL,
                sort_order INTEGER NOT NULL DEFAULT 0,
                is_active INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes')),
                updated_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes'))
            );

            CREATE TABLE IF NOT EXISTS restaurant_tables (
                id INTEGER PRIMARY KEY,
                uuid TEXT NOT NULL DEFAULT (uuid()),
                client_id INTEGER NOT NULL DEFAULT 1,
                table_number TEXT NOT NULL,
                area_id INTEGER,
                qr_code TEXT,
                qr_token TEXT,
                table_status TEXT NOT NULL DEFAULT 'available',
                current_amount REAL NOT NULL DEFAULT 0,
                order_timestamp INTEGER,
                is_active INTEGER NOT NULL DEFAULT 1,
                sync_version INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes')),
                updated_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes')),
                FOREIGN KEY(area_id) REFERENCES dining_areas(id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS table_client_states (
                id INTEGER PRIMARY KEY,
                client_id INTEGER NOT NULL DEFAULT 1,
                table_id INTEGER NOT NULL,
                table_status TEXT NOT NULL DEFAULT 'available',
                current_amount REAL NOT NULL DEFAULT 0,
                order_timestamp INTEGER,
                updated_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes')),
                UNIQUE(client_id, table_id),
                FOREIGN KEY(table_id) REFERENCES restaurant_tables(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS orders (
                id INTEGER PRIMARY KEY,
                uuid TEXT NOT NULL DEFAULT (uuid()),
                client_id INTEGER NOT NULL DEFAULT 1,
                table_id INTEGER,
                customer_id INTEGER,
                created_by INTEGER,
                order_status TEXT NOT NULL DEFAULT 'pending',
                total_amount REAL NOT NULL DEFAULT 0,
                discount_amount REAL NOT NULL DEFAULT 0,
                discount_type TEXT,
                discount_value REAL NOT NULL DEFAULT 0,
                discount_label TEXT,
                discount_date TEXT,
                discount_start_time TEXT,
                discount_end_time TEXT,
                discount_is_paused INTEGER NOT NULL DEFAULT 0,
                customer_name TEXT,
                customer_mobile TEXT,
                bill_note TEXT,
                is_kot_only INTEGER NOT NULL DEFAULT 1,
                report_visible INTEGER NOT NULL DEFAULT 0,
                billed_at TEXT,
                bill_number INTEGER,
                sync_version INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes')),
                updated_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes')),
                FOREIGN KEY(table_id) REFERENCES restaurant_tables(id) ON DELETE SET NULL,
                FOREIGN KEY(customer_id) REFERENCES customers(id) ON DELETE SET NULL,
                UNIQUE(client_id, bill_number)
            );

            CREATE TABLE IF NOT EXISTS order_items (
                id INTEGER PRIMARY KEY,
                uuid TEXT NOT NULL DEFAULT (uuid()),
                order_id INTEGER NOT NULL,
                item_id INTEGER,
                client_item_id TEXT,
                item_name TEXT,
                price REAL NOT NULL DEFAULT 0,
                quantity INTEGER NOT NULL DEFAULT 1,
                is_parcel INTEGER NOT NULL DEFAULT 0,
                total REAL NOT NULL DEFAULT 0,
                discount_amount REAL NOT NULL DEFAULT 0,
                discount_type TEXT,
                discount_value REAL NOT NULL DEFAULT 0,
                discount_label TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes')),
                updated_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes')),
                FOREIGN KEY(order_id) REFERENCES orders(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS order_status_logs (
                id INTEGER PRIMARY KEY,
                order_id INTEGER NOT NULL,
                status TEXT NOT NULL,
                changed_by INTEGER,
                created_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes')),
                FOREIGN KEY(order_id) REFERENCES orders(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value_json TEXT NOT NULL,
                updated_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes'))
            );

            CREATE TABLE IF NOT EXISTS sync_queue (
                id INTEGER PRIMARY KEY,
                entity_type TEXT NOT NULL,
                entity_id TEXT,
                operation TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'pending',
                attempts INTEGER NOT NULL DEFAULT 0,
                next_attempt_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes')),
                last_error TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes')),
                updated_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes'))
            );

            CREATE TABLE IF NOT EXISTS sync_logs (
                id INTEGER PRIMARY KEY,
                queue_id INTEGER,
                level TEXT NOT NULL,
                message TEXT NOT NULL,
                details_json TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes'))
            );

            CREATE TABLE IF NOT EXISTS backup_history (
                id INTEGER PRIMARY KEY,
                backup_path TEXT NOT NULL,
                kind TEXT NOT NULL,
                size_bytes INTEGER NOT NULL DEFAULT 0,
                status TEXT NOT NULL,
                message TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes'))
            );

            CREATE INDEX IF NOT EXISTS idx_orders_created_at ON orders(created_at);
            CREATE INDEX IF NOT EXISTS idx_orders_status_created_at ON orders(order_status, created_at);
            CREATE INDEX IF NOT EXISTS idx_orders_client_created_at ON orders(client_id, created_at);
            CREATE INDEX IF NOT EXISTS idx_orders_client_table_status ON orders(client_id, table_id, order_status);
            CREATE INDEX IF NOT EXISTS idx_orders_client_billed_at ON orders(client_id, billed_at);
            CREATE INDEX IF NOT EXISTS idx_order_items_order_id ON order_items(order_id);
            CREATE INDEX IF NOT EXISTS idx_table_client_states_table ON table_client_states(table_id);
            CREATE INDEX IF NOT EXISTS idx_sync_queue_status_next ON sync_queue(status, next_attempt_at);"));

        Apply(conn, 2, "sqlite_pos_bootstrap_cache", m => m.Exec(@"
            CREATE TABLE IF NOT EXISTS categories (
                id INTEGER PRIMARY KEY,
                uuid TEXT NOT NULL DEFAULT (uuid()),
                client_id INTEGER NOT NULL DEFAULT 1,
                name TEXT NOT NULL,
                image TEXT,
                parent_id INTEGER,
                sort_order INTEGER NOT NULL DEFAULT 0,
                is_active INTEGER NOT NULL DEFAULT 1,
                sync_version INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes')),
                updated_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes'))
            );

            CREATE TABLE IF NOT EXISTS menu_items (
                id INTEGER PRIMARY KEY,
                uuid TEXT NOT NULL DEFAULT (uuid()),
                client_id INTEGER NOT NULL DEFAULT 1,
                category_id INTEGER,
                name TEXT NOT NULL,
                code TEXT,
                price REAL NOT NULL DEFAULT 0,
                description TEXT,
                image TEXT,
                type TEXT NOT NULL DEFAULT 'veg',
                is_available INTEGER NOT NULL DEFAULT 1,
                is_parcel INTEGER NOT NULL DEFAULT 0,
                sort_order INTEGER NOT NULL DEFAULT 0,
                sync_version INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes')),
                updated_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes')),
                FOREIGN KEY(category_id) REFERENCES categories(id) ON DELETE SET NULL
            );

            CREATE INDEX IF NOT EXISTS idx_menu_items_category ON menu_items(category_id);
            CREATE INDEX IF NOT EXISTS idx_menu_items_code ON menu_items(code);
            CREATE INDEX IF NOT EXISTS idx_categories_parent ON categories(parent_id);"));

        Apply(conn, 3, "sqlite_pos_ledger_and_gst_rates", m => m.Exec(@"
            CREATE TABLE IF NOT EXISTS ledger_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                customer_id INTEGER NOT NULL,
                amount REAL NOT NULL,
                type TEXT NOT NULL,
                note TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes')),
                updated_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes')),
                FOREIGN KEY(customer_id) REFERENCES customers(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS gst_rates (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                client_id INTEGER NOT NULL DEFAULT 1,
                name TEXT NOT NULL,
                rate REAL NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes'))
            );"));

        Apply(conn, 4, "sqlite_pos_client_id_indexes", m => m.Exec(@"
            CREATE INDEX IF NOT EXISTS idx_categories_client ON categories(client_id);
            CREATE INDEX IF NOT EXISTS idx_menu_items_client ON menu_items(client_id);
            CREATE INDEX IF NOT EXISTS idx_tables_client ON restaurant_tables(client_id);
            CREATE INDEX IF NOT EXISTS idx_dining_areas_client ON dining_areas(client_id);
            CREATE INDEX IF NOT EXISTS idx_gst_rates_client ON gst_rates(client_id);"));

        Apply(conn, 5, "sqlite_pos_live_sync_columns", m =>
        {
            m.AddColumnIfMissing("orders", "live_sync_status", "TEXT DEFAULT 'not_applicable'");
            m.AddColumnIfMissing("orders", "live_sync_at", "TEXT");
            m.AddColumnIfMissing("orders", "live_server_id", "INTEGER");
            m.AddColumnIfMissing("orders", "live_sync_error", "TEXT");
            m.AddColumnIfMissing("orders", "live_sync_attempts", "INTEGER DEFAULT 0");
            m.Exec("CREATE INDEX IF NOT EXISTS idx_orders_live_sync ON orders(live_sync_status, order_status);");
            m.Exec(@"UPDATE orders SET live_sync_status = 'pending'
                WHERE order_status IN ('completed', 'settled')
                  AND report_visible = 1 AND is_kot_only = 0
                  AND billed_at IS NOT NULL AND live_sync_status = 'not_applicable';");
            m.Exec(@"UPDATE orders SET live_sync_status = 'not_applicable'
                WHERE live_sync_status = 'not_applicable'
                  AND (order_status NOT IN ('completed', 'settled') OR report_visible = 0 OR is_kot_only = 1);");
        });

        Apply(conn, 6, "sqlite_pos_unify_ledger_debit_credit", m =>
        {
            m.Exec("UPDATE ledger_entries SET type = 'debit' WHERE type = 'credit';");
            m.Exec("UPDATE ledger_entries SET type = 'credit' WHERE type = 'payment';");
        });

        Apply(conn, 7, "sqlite_pos_sub_category_column", m =>
        {
            m.AddColumnIfMissing("menu_items", "sub_category_id", "INTEGER");
            m.Exec("CREATE INDEX IF NOT EXISTS idx_menu_items_subcategory ON menu_items(sub_category_id);");
        });

        Apply(conn, 8, "sqlite_pos_is_parcel_mode_column", m =>
            m.AddColumnIfMissing("orders", "is_parcel_mode", "INTEGER NOT NULL DEFAULT 0"));

        Apply(conn, 9, "sqlite_pos_order_items_indexes", m => m.Exec(@"
            CREATE INDEX IF NOT EXISTS idx_order_items_item_id ON order_items(item_id);
            CREATE INDEX IF NOT EXISTS idx_order_items_client_item_id ON order_items(client_item_id);"));

        Apply(conn, 10, "sqlite_pos_billed_at_utc_to_ist", m => m.Exec(
            "UPDATE orders SET billed_at = strftime('%Y-%m-%d %H:%M:%S', billed_at, '+330 minutes') " +
            "WHERE billed_at LIKE '%T%';"));

        Apply(conn, 11, "sqlite_pos_ledger_live_sync_columns", m =>
        {
            m.AddColumnIfMissing("customers", "live_sync_status", "TEXT DEFAULT 'pending'");
            m.AddColumnIfMissing("customers", "live_id", "INTEGER");
            if (m.AddColumnIfMissing("ledger_entries", "uuid", "TEXT"))
            {
                m.Exec("UPDATE ledger_entries SET uuid = lower(hex(randomblob(16))) WHERE uuid IS NULL;");
            }
            m.AddColumnIfMissing("ledger_entries", "live_sync_status", "TEXT DEFAULT 'pending'");
            m.Exec("CREATE INDEX IF NOT EXISTS idx_customers_live_sync ON customers(live_sync_status);");
            m.Exec("CREATE INDEX IF NOT EXISTS idx_ledger_entries_live_sync ON ledger_entries(live_sync_status);");
            m.Exec("CREATE UNIQUE INDEX IF NOT EXISTS idx_ledger_entries_uuid ON ledger_entries(uuid);");
        });

        Apply(conn, 12, "sqlite_pos_ledger_sync_crud", m =>
        {
            m.AddColumnIfMissing("ledger_entries", "live_id", "INTEGER");
            m.Exec(@"CREATE TABLE IF NOT EXISTS ledger_deletions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                entity_type TEXT NOT NULL,
                live_id INTEGER NOT NULL,
                created_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes'))
            );");
        });

        Apply(conn, 13, "sqlite_pos_ledger_balance_patch", m =>
        {
            m.AddColumnIfMissing("customers", "address", "TEXT");
            m.AddColumnIfMissing("customers", "phone", "TEXT");
            m.AddColumnIfMissing("customers", "balance", "REAL NOT NULL DEFAULT 0");
            m.AddColumnIfMissing("ledger_entries", "client_id", "INTEGER NOT NULL DEFAULT 1");
            m.AddColumnIfMissing("ledger_entries", "payment_mode", "TEXT NOT NULL DEFAULT 'cash'");
            m.AddColumnIfMissing("ledger_entries", "remarks", "TEXT");
            m.Exec(@"CREATE TABLE IF NOT EXISTS ledger_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                client_id INTEGER NOT NULL DEFAULT 1,
                customer_id INTEGER NOT NULL,
                type TEXT NOT NULL DEFAULT 'gave',
                amount REAL NOT NULL DEFAULT 0,
                payment_mode TEXT NOT NULL DEFAULT 'cash',
                remarks TEXT,
                created_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes')),
                FOREIGN KEY(customer_id) REFERENCES customers(id) ON DELETE CASCADE
            );");
        });

        Apply(conn, 14, "sqlite_pos_quick_notes", m =>
        {
            m.Exec(@"CREATE TABLE IF NOT EXISTS quick_notes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                client_id INTEGER NOT NULL DEFAULT 1,
                customer_name TEXT,
                customer_mobile TEXT,
                saved_time TEXT NOT NULL,
                target_time TEXT,
                total_qty INTEGER NOT NULL DEFAULT 0,
                grand_total REAL NOT NULL DEFAULT 0,
                items_json TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes'))
            );");
            m.AddColumnIfMissing("quick_notes", "target_time", "TEXT");
        });

        Apply(conn, 15, "sqlite_pos_quick_notes_target_time", m =>
        {
            m.AddColumnIfMissing("quick_notes", "target_time", "TEXT");
        });

        // Who the operators are, mirrored from the server so a bill can record who took it.
        // No password or pin column: this file sits unencrypted on the counter, and the
        // server deliberately doesn't send credentials down (see SyncController::clientUsers).
        Apply(conn, 16, "sqlite_pos_users", m =>
        {
            m.Exec(@"CREATE TABLE IF NOT EXISTS users (
                id INTEGER PRIMARY KEY,
                uuid TEXT,
                client_id INTEGER NOT NULL DEFAULT 1,
                name TEXT NOT NULL DEFAULT '',
                phone TEXT,
                email TEXT,
                role TEXT,
                is_active INTEGER NOT NULL DEFAULT 1,
                created_at TEXT DEFAULT (datetime('now', '+330 minutes')),
                updated_at TEXT DEFAULT (datetime('now', '+330 minutes'))
            );");
            m.Exec("CREATE INDEX IF NOT EXISTS idx_users_client ON users(client_id);");
        });

        // The PIN's bcrypt hash, so a counter with no line can still sign someone in.
        // Never the PIN itself — see SyncController::clientUsers on the server side.
        Apply(conn, 17, "sqlite_pos_users_pin", m =>
        {
            m.AddColumnIfMissing("users", "pin", "TEXT");
        });

        // Settings that belong to a BUSINESS rather than to this machine.
        //
        // app_settings is keyed by name alone, which was fine while the till served one client.
        // With two brands sharing the counter it is not: the printed header, GST number, FSSAI
        // licence and UPI id are different for each, and one shared row would print Daal Roti's
        // GST number on a Chay Chaupal bill. The server has always kept these per client
        // (app_settings.client_id there); this is the local side catching up.
        //
        // Machine settings — printer, paper size, window layout, server address, last signed-in
        // number — deliberately stay in app_settings. They describe this counter, not a brand,
        // and must not change when a different business signs in.
        Apply(conn, 18, "sqlite_pos_client_settings", m =>
        {
            m.Exec(@"CREATE TABLE IF NOT EXISTS client_settings (
                client_id INTEGER NOT NULL,
                key TEXT NOT NULL,
                value_json TEXT NOT NULL,
                updated_at TEXT NOT NULL DEFAULT (datetime('now', '+330 minutes')),
                PRIMARY KEY (client_id, key)
            );");

            // Everything already on this till was billed as client 1, so that is whose profile
            // the existing values are. Copied rather than moved: an older build still reading
            // app_settings keeps working until it is replaced.
            m.Exec(@"INSERT OR IGNORE INTO client_settings (client_id, key, value_json, updated_at)
                     SELECT 1, key, value_json, updated_at
                     FROM app_settings
                     -- Exactly BootstrapSyncService.SyncedSettingKeys: the set the server
                     -- already treats as belonging to the business. Keeping the two lists the
                     -- same is what stops a machine setting from being pulled across counters.
                     WHERE key IN (
                        'restaurant_profile', 'upi_settings', 'daily_reset_bill_counter',
                        'login_pin', 'pos_wpf_settings', 'pos_wpf_shortcuts'
                     );");
        });

        // The bill-number prefix — the letters in #CC-0007 — abbreviated from the business name.
        //
        // A versioned migration rather than one of the AddColumnIfMissing calls at the top of
        // Migrate(): that block runs BEFORE migration 1 creates the clients table, and
        // AddColumnIfMissing quietly does nothing when the table is missing. On a fresh install
        // the column would therefore not appear until the app's SECOND launch, and every bill
        // taken on the first one would fail on "no such column: bill_prefix".
        //
        // Left empty rather than backfilled: BillPrefix.Resolve fills each row in on first use,
        // so a till that has never synced its profile doesn't get stamped with a guess.
        Apply(conn, 19, "sqlite_pos_client_bill_prefix", m =>
        {
            m.AddColumnIfMissing("clients", "bill_prefix", "TEXT");
        });

        Apply(conn, 20, "sqlite_pos_prevent_sync_storm_old_orders", m =>
        {
            m.Exec("UPDATE orders SET live_sync_status = 'synced' WHERE billed_at IS NOT NULL AND date(billed_at) < '2026-08-04';");
            m.Exec(@"DELETE FROM sync_queue 
                     WHERE entity_type IN ('order', 'table_order') 
                       AND entity_id IN (SELECT cast(id as text) FROM orders WHERE billed_at IS NOT NULL AND date(billed_at) < '2026-08-04');");
        });

        Apply(conn, 21, "sqlite_pos_resync_today_orders", m =>
        {
            m.Exec(@"DELETE FROM sync_queue 
                     WHERE entity_type IN ('order', 'table_order') 
                       AND entity_id IN (SELECT cast(id as text) FROM orders WHERE billed_at IS NOT NULL AND date(billed_at) = '2026-08-05');");

            m.Exec("UPDATE orders SET live_sync_status = 'pending' WHERE billed_at IS NOT NULL AND date(billed_at) = '2026-08-05';");

            m.Exec(@"
                INSERT INTO sync_queue (entity_type, entity_id, operation, payload_json, status, created_at, next_attempt_at)
                SELECT 
                    'order',
                    cast(o.id as text),
                    'insert',
                    json_object(
                        'id', o.id,
                        'sqlite_uuid', o.uuid,
                        'client_id', o.client_id,
                        'table_id', o.table_id,
                        'order_status', o.order_status,
                        'total_amount', o.total_amount,
                        'discount_amount', o.discount_amount,
                        'discount_type', o.discount_type,
                        'discount_value', o.discount_value,
                        'discount_label', o.discount_label,
                        'customer_name', o.customer_name,
                        'customer_mobile', o.customer_mobile,
                        'bill_note', o.bill_note,
                        'is_kot_only', o.is_kot_only,
                        'report_visible', o.report_visible,
                        'billed_at', o.billed_at,
                        'bill_number', o.bill_number,
                        'is_parcel_mode', o.is_parcel_mode,
                        'created_at', o.created_at,
                        'updated_at', o.updated_at,
                        'items', (
                            SELECT json_group_array(
                                json_object(
                                    'id', oi.id,
                                    'order_id', oi.order_id,
                                    'item_id', oi.item_id,
                                    'client_item_id', oi.client_item_id,
                                    'item_name', oi.item_name,
                                    'price', oi.price,
                                    'quantity', oi.quantity,
                                    'is_parcel', oi.is_parcel,
                                    'total', oi.total,
                                    'discount_amount', oi.discount_amount,
                                    'discount_type', oi.discount_type,
                                    'discount_value', oi.discount_value,
                                    'discount_label', oi.discount_label
                                )
                            )
                            FROM order_items oi
                            WHERE oi.order_id = o.id
                        )
                    ),
                    'pending',
                    datetime('now', '+330 minutes'),
                    datetime('now', '+330 minutes')
                FROM orders o
                WHERE o.billed_at IS NOT NULL AND date(o.billed_at) = '2026-08-05';
            ");
        });
    }

    private static void Apply(SqliteConnection conn, int version, string name, Action<Ctx> work)
    {
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT version FROM schema_migrations WHERE version = $v";
            check.Parameters.AddWithValue("$v", version);
            var existing = check.ExecuteScalar();
            if (existing != null && existing != DBNull.Value)
            {
                return;
            }
        }

        using var tx = conn.BeginTransaction();
        var ctx = new Ctx(conn, tx);
        work(ctx);

        using (var insert = conn.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO schema_migrations (version, name) VALUES ($v, $n)";
            insert.Parameters.AddWithValue("$v", version);
            insert.Parameters.AddWithValue("$n", name);
            insert.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// Migration context: every command it runs is bound to the active
    /// transaction, which Microsoft.Data.Sqlite requires while a transaction is
    /// open on the connection.
    /// </summary>
    private sealed class Ctx
    {
        private readonly SqliteConnection _conn;
        private readonly SqliteTransaction _tx;

        public Ctx(SqliteConnection conn, SqliteTransaction tx)
        {
            _conn = conn;
            _tx = tx;
        }

        public void Exec(string sql)
        {
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = _tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private bool TableExists(string table)
        {
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = _tx;
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=$table;";
            cmd.Parameters.AddWithValue("$table", table);
            var res = cmd.ExecuteScalar();
            return res != null && res != DBNull.Value;
        }

        /// <summary>Adds a column only if missing. Returns true if it was added.</summary>
        public bool AddColumnIfMissing(string table, string column, string definition)
        {
            if (!TableExists(table))
            {
                return false;
            }
            if (ColumnExists(table, column))
            {
                return false;
            }
            Exec($"ALTER TABLE {table} ADD COLUMN {column} {definition};");
            return true;
        }

        private bool ColumnExists(string table, string column)
        {
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = _tx;
            cmd.CommandText = $"PRAGMA table_info({table});";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
