using Dapper;
using Pos.Core.Data;
using Pos.Core.Models;
using Pos.Core.Repositories;
using Pos.Core.Sync;

// --sync: asli DB par ek quick bill banao aur use server tak bhejo (UI ko chhede bina)
//
// Baaki harness throwaway temp DB par chalta hai; sirf yahi asli till ki DB kholta hai aur ek
// SACHCHA bill banata hai jo bill number kharch karta hai aur server tak chala jaata hai. Isi
// liye ismein confirm maangta hai — galti se production counter par chal gaya to us din ki
// takings mein ek jhootha bill jud jaayega.
if (args.Contains("--sync"))
{
    var realDb = new DatabaseService(DatabaseService.DefaultDbPath());
    Console.WriteLine($"DB     : {realDb.DbPath}");
    Console.WriteLine("WARNING: yeh ASLI database hai. Ek real bill banega aur server par jaayega.");

    if (!args.Contains("--yes"))
    {
        Console.Write("Aage badhein? 'yes' likhiye: ");
        if ((Console.ReadLine() ?? "").Trim().ToLowerInvariant() is not ("yes" or "y"))
        {
            Console.WriteLine("Radd kiya — kuch nahi likha gaya.");
            return;
        }
    }

    // Jo client sign-in par chun'a jaata hai wahi yahan bhi — warna bill doosre brand ke khaate
    // mein chala jaayega. ClientContext ki default id 1 hai, aur yahi till ka purana behaviour.
    var realClient = new ClientContext();
    var settings = new AppSettingsRepository(realDb, realClient);
    var orderRepo = new OrderRepository(realDb, realClient);
    var menuRepo = new MenuRepository(realDb);

    var item = menuRepo.GetMenuItems().First();
    Console.WriteLine($"item   : {item.Name} @ {item.Price}");

    var saved = orderRepo.SaveFinalOrder(new TableOrderPayload
    {
        TableId = null,
        TableStatus = "completed",
        Items = { new OrderItemInput { ItemId = item.Id, ItemName = item.Name, Price = item.Price, Quantity = 2 } }
    });
    Console.WriteLine($"bill   : {saved.FormattedBillNumber}, order {saved.Id}, total {saved.TotalAmount}");

    var api = new PosApiClient(new SyncCoordinator(realDb, settings, realClient).ApiUrl,
                               realClient.Slug, realClient.ClientId);
    var pull = await new BootstrapSyncService(realDb, api).PullAsync();
    Console.WriteLine($"pull   : ok={pull.Ok} cats={pull.Categories} items={pull.MenuItems} tables={pull.Tables} areas={pull.Areas} err={pull.Error ?? "-"}");

    var coordinator = new SyncCoordinator(realDb, settings, realClient);
    Console.WriteLine($"api    : {coordinator.ApiUrl}");
    var status = await coordinator.SyncNowAsync();
    Console.WriteLine($"sync   : online={status.Online} pending={status.Pending} lastError={status.LastError ?? "-"}");
    Console.WriteLine($"time   : IST {Ist.Stamp()}");
    return;
}

if (args.Contains("--inspect"))
{
    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ChayChaupalPOS", "sqlite");
    Console.WriteLine($"Inspecting directory: {dir}");
    if (Directory.Exists(dir))
    {
        foreach (var file in Directory.GetFiles(dir, "*.sqlite3"))
        {
            Console.WriteLine($"\n========================================================");
            Console.WriteLine($"DATABASE FILE: {Path.GetFileName(file)}");
            Console.WriteLine($"========================================================");
            try
            {
                var inspectDb = new DatabaseService(file);
                using var conn = inspectDb.OpenConnection();
                
                // Print tables
                var tableNames = conn.Query<string>("SELECT name FROM sqlite_master WHERE type='table'");
                Console.WriteLine($"Tables: {string.Join(", ", tableNames)}");

                if (tableNames.Contains("orders"))
                {
                    var total = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM orders");
                    Console.WriteLine($"Total orders: {total}");
                    var syncDist = conn.Query<dynamic>("SELECT live_sync_status, COUNT(*) AS cnt FROM orders GROUP BY live_sync_status");
                    foreach (var row in syncDist)
                    {
                        Console.WriteLine($"  Sync Status: {row.live_sync_status}, Count: {row.cnt}");
                    }
                    var samplePending = conn.Query<dynamic>("SELECT id, client_id, bill_number, total_amount, order_status, billed_at, created_at, uuid FROM orders WHERE live_sync_status = 'pending' LIMIT 3");
                    if (samplePending.Any()) Console.WriteLine("Sample pending orders:");
                    foreach (var o in samplePending)
                    {
                        Console.WriteLine($"  ID: {o.id}, Client: {o.client_id}, Bill#: {o.bill_number}, Total: {o.total_amount}, Status: {o.order_status}, BilledAt: {o.billed_at}, CreatedAt: {o.created_at}, UUID: {o.uuid}");
                    }
                }

                if (tableNames.Contains("client_settings"))
                {
                    Console.WriteLine("\nClient Settings:");
                    var settingsList = conn.Query<dynamic>("SELECT client_id, key, length(value_json) as val_len, value_json, updated_at FROM client_settings");
                    foreach (var s in settingsList)
                    {
                        Console.WriteLine($"  Client: {s.client_id}, Key: {s.key}, Len: {s.val_len}, UpdatedAt: {s.updated_at}, Value: {s.value_json}");
                    }
                }

                if (tableNames.Contains("app_settings"))
                {
                    Console.WriteLine("\nApp Settings:");
                    var appSettingsList = conn.Query<dynamic>("SELECT key, length(value_json) as val_len, value_json, updated_at FROM app_settings");
                    foreach (var s in appSettingsList)
                    {
                        Console.WriteLine($"  Key: {s.key}, Len: {s.val_len}, UpdatedAt: {s.updated_at}, Value: {s.value_json}");
                    }
                }

                if (tableNames.Contains("clients"))
                {
                    Console.WriteLine("\nClients:");
                    var clientsList = conn.Query<dynamic>("SELECT id, name, slug FROM clients");
                    foreach (var c in clientsList)
                    {
                        Console.WriteLine($"  ID: {c.id}, Name: {c.name}, Slug: {c.slug}");
                    }
                }

                if (tableNames.Contains("users"))
                {
                    Console.WriteLine("\nUsers:");
                    var usersList = conn.Query<dynamic>("SELECT id, name, role, is_active, pin FROM users");
                    foreach (var u in usersList)
                    {
                        Console.WriteLine($"  ID: {u.id}, Name: {u.name}, Role: {u.role}, Active: {u.is_active}, Pin: {u.pin}");
                    }
                }

                if (tableNames.Contains("customers"))
                {
                    try
                    {
                        var totalCust = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM customers");
                        Console.WriteLine($"\nTotal customers: {totalCust}");
                        var sampleCust = conn.Query<dynamic>("SELECT id, name, mobile, balance FROM customers LIMIT 5");
                        foreach (var c in sampleCust)
                        {
                            Console.WriteLine($"  Customer ID: {c.id}, Name: {c.name}, Mobile: {c.mobile}, Balance: {c.balance}");
                        }
                    }
                    catch
                    {
                        var totalCust = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM customers");
                        Console.WriteLine($"\nTotal customers (old schema): {totalCust}");
                        var sampleCust = conn.Query<dynamic>("SELECT id, name, mobile FROM customers LIMIT 5");
                        foreach (var c in sampleCust)
                        {
                            Console.WriteLine($"  Customer ID: {c.id}, Name: {c.name}, Mobile: {c.mobile}");
                        }
                    }
                }

                if (tableNames.Contains("ledger_entries"))
                {
                    var totalEntries = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM ledger_entries");
                    Console.WriteLine($"Total ledger entries: {totalEntries}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error reading database: {ex.Message}");
            }
        }
    }
    return;
}

// Dev-only harness: exercise the full MVP order flow against a throwaway DB.
DapperConfig.Init();
var tmp = Path.Combine(Path.GetTempPath(), $"pos_dev_{Guid.NewGuid():N}", "pos.sqlite3");
Console.WriteLine($"Temp DB: {tmp}\n");

var db = new DatabaseService(tmp);
new SqliteMigrationRunner(db).Migrate();

// Seed minimal data: a client, a table, two menu items.
using (var conn = db.OpenConnection())
{
    conn.Execute("INSERT INTO clients (id, slug, name) VALUES (1, 'chaychaupal', 'Chay Chaupal')");
    conn.Execute("INSERT INTO restaurant_tables (id, client_id, table_number) VALUES (5, 1, '5')");
    conn.Execute("INSERT INTO categories (id, client_id, name) VALUES (1, 1, 'Tea')");
    conn.Execute("INSERT INTO menu_items (id, client_id, category_id, name, price) VALUES (10, 1, 1, 'Masala Chai', 15)");
    conn.Execute("INSERT INTO menu_items (id, client_id, category_id, name, price) VALUES (11, 1, 1, 'Samosa', 20)");
}

// Har repository ko batana padta hai kis business ke liye bill ban raha hai. Yahan wahi client
// jo upar seed hua — id 1, "Chay Chaupal" — taaki bill prefix bhi usi naam se bane.
var client = new ClientContext();
client.Use(1, "chaychaupal", "Chay Chaupal");

var menu = new MenuRepository(db);
var tables = new TableRepository(db, client);
var orders = new OrderRepository(db, client);

Console.WriteLine($"Menu items: {menu.GetMenuItems().Count}, Categories: {menu.GetCategories().Count}");
Console.WriteLine($"next bill: {orders.NextBillNumber().FormattedBillNumber}\n");

// 1) KOT: table 5, 2x Chai + 1x Samosa
var kot = orders.SaveTableOrder(new TableOrderPayload
{
    TableId = 5,
    TableStatus = "ordered",
    Items =
    {
        new OrderItemInput { ItemId = 10, ItemName = "Masala Chai", Price = 15, Quantity = 2 },
        new OrderItemInput { ItemId = 11, ItemName = "Samosa", Price = 20, Quantity = 1 },
    }
});
Console.WriteLine($"KOT saved: orderId={kot.Id}, total={kot.TotalAmount}, tableStatus={kot.TableStatus}, billNo={kot.BillNumber?.ToString() ?? "null"}");

var active = orders.GetActiveOrderForTable(5)!;
Console.WriteLine($"  active order items={active.Items.Count}, is_kot_only={active.IsKotOnly}, report_visible={active.ReportVisible}");
DumpTable(db, client, 5);

// 2) Add another KOT (replace-mode default): 1x Chai + 1x Samosa + 1x Chai
var kot2 = orders.SaveTableOrder(new TableOrderPayload
{
    TableId = 5,
    TableStatus = "ordered",
    Items =
    {
        new OrderItemInput { ItemId = 10, ItemName = "Masala Chai", Price = 15, Quantity = 3 },
        new OrderItemInput { ItemId = 11, ItemName = "Samosa", Price = 20, Quantity = 2 },
    }
});
active = orders.GetActiveOrderForTable(5)!;
Console.WriteLine($"\nKOT#2 (same order {kot2.Id == kot.Id}): total={kot2.TotalAmount}, items={active.Items.Count}");

// 3) Final bill: status completed
var bill = orders.SaveTableOrder(new TableOrderPayload
{
    TableId = 5,
    TableStatus = "completed",
    Items =
    {
        new OrderItemInput { ItemId = 10, ItemName = "Masala Chai", Price = 15, Quantity = 3 },
        new OrderItemInput { ItemId = 11, ItemName = "Samosa", Price = 20, Quantity = 2 },
    }
});
var billed = orders.GetActiveOrderForTable(5);
Console.WriteLine($"\nBill saved: orderId={bill.Id}, billNo={bill.FormattedBillNumber}, total={bill.TotalAmount}");
using (var conn = db.OpenConnection())
{
    var row = conn.QueryFirst<dynamic>("SELECT is_kot_only, report_visible, billed_at, bill_number, order_status FROM orders WHERE id = @id", new { id = bill.Id });
    Console.WriteLine($"  is_kot_only={row.is_kot_only}, report_visible={row.report_visible}, billed_at={row.billed_at}, order_status={row.order_status}");
}

// 4) Clear table
var clear = orders.SaveTableOrder(new TableOrderPayload { TableId = 5, TableStatus = "available", Items = new() });
Console.WriteLine($"\nClear: cleared={clear.Cleared}, tableStatus={clear.TableStatus}");
DumpTable(db, client, 5);

using (var conn = db.OpenConnection())
{
    var q = conn.ExecuteScalar<long>("SELECT COUNT(*) FROM sync_queue");
    var settled = conn.ExecuteScalar<long>("SELECT COUNT(*) FROM orders WHERE order_status='settled'");
    Console.WriteLine($"\nsync_queue rows={q}, settled orders={settled}");
}

// 5) Receipt layout check — prints to console so column alignment can be verified
//    without burning paper. Ruler line shows the expected column count.
PrintReceiptSamples();

Console.WriteLine("\nOK");

static void PrintReceiptSamples()
{
    var cfg = new Pos.Core.Printing.PrintConfig
    {
        PaperSize = "80mm",
        StoreName = "Chay Chaupal",
        Phone = "9628717175",
        GstNo = "09AAAAA0000A1Z1",
        Address = "Varanasi, Uttar Pradesh",
        ShowName = true, ShowPhone = true, ShowGst = true, ShowAddress = true
    };
    var b = new Pos.Core.Printing.ReceiptBuilder(cfg);
    var items = new List<Pos.Core.Printing.PrintLine>
    {
        new("Masala Chai", 2, 15, false),
        new("Samosa", 1, 20, false),
        new("Veg Sandwich Extra Long Name Here", 3, 60, false),
        new("Paneer Roll", 2, 90, true),
    };
    var stamp = new DateTime(2026, 7, 26, 14, 35, 0);

    void Dump(string title, string text)
    {
        Console.WriteLine($"\n===== {title} ({b.Cols} cols) =====");
        Console.WriteLine(new string('=', b.Cols));
        foreach (var l in text.TrimEnd().Split('\n'))
            Console.WriteLine($"{l.TrimEnd('\r')}|{l.TrimEnd('\r').Length}");
        Console.WriteLine(new string('=', b.Cols));
    }

    Dump("KOT", b.BuildKot(items, "T-5", "Less sugar", stamp));
    Dump("BILL", b.BuildBill(items, "#CC-0042", "5", 25, 335, stamp));

    var cfg58 = new Pos.Core.Printing.PrintConfig { PaperSize = "58mm", StoreName = "Chay Chaupal", ShowName = true };
    var b58 = new Pos.Core.Printing.ReceiptBuilder(cfg58);
    Console.WriteLine($"\n===== BILL 58mm ({b58.Cols} cols) =====");
    foreach (var l in b58.BuildBill(items, "#CC-0042", "5", 0, 335, stamp).TrimEnd().Split('\n'))
        Console.WriteLine($"{l.TrimEnd('\r')}|{l.TrimEnd('\r').Length}");
}

static void DumpTable(DatabaseService db, ClientContext client, long tableId)
{
    var view = new TableRepository(db, client).All().FirstOrDefault(t => t.Id == tableId);
    if (view != null)
        Console.WriteLine($"  table {view.TableNumber}: status={view.Status}, amount={view.Amount}");
}
