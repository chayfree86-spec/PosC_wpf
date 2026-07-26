using Dapper;
using Pos.Core.Data;
using Pos.Core.Models;
using Pos.Core.Repositories;
using Pos.Core.Sync;

// --sync: asli DB par ek quick bill banao aur use server tak bhejo (UI ko chhede bina)
if (args.Contains("--sync"))
{
    var realDb = new DatabaseService(DatabaseService.DefaultDbPath());
    var settings = new AppSettingsRepository(realDb);
    var orderRepo = new OrderRepository(realDb);
    var menuRepo = new MenuRepository(realDb);

    var item = menuRepo.GetMenuItems().First();
    Console.WriteLine($"DB     : {realDb.DbPath}");
    Console.WriteLine($"item   : {item.Name} @ {item.Price}");

    var saved = orderRepo.SaveFinalOrder(new TableOrderPayload
    {
        TableId = null,
        TableStatus = "completed",
        Items = { new OrderItemInput { ItemId = item.Id, ItemName = item.Name, Price = item.Price, Quantity = 2 } }
    });
    Console.WriteLine($"bill   : #{saved.BillNumber}, order {saved.Id}, total {saved.TotalAmount}");

    var api = new PosApiClient(new SyncCoordinator(realDb, settings).ApiUrl);
    var pull = await new BootstrapSyncService(realDb, api).PullAsync();
    Console.WriteLine($"pull   : ok={pull.Ok} cats={pull.Categories} items={pull.MenuItems} tables={pull.Tables} areas={pull.Areas} err={pull.Error ?? "-"}");

    var coordinator = new SyncCoordinator(realDb, settings);
    Console.WriteLine($"api    : {coordinator.ApiUrl}");
    var status = await coordinator.SyncNowAsync();
    Console.WriteLine($"sync   : online={status.Online} pending={status.Pending} lastError={status.LastError ?? "-"}");
    Console.WriteLine($"time   : IST {Ist.Stamp()}");
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

var menu = new MenuRepository(db);
var tables = new TableRepository(db);
var orders = new OrderRepository(db);

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
DumpTable(db, 5);

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
Console.WriteLine($"\nBill saved: orderId={bill.Id}, billNo={bill.BillNumber}, total={bill.TotalAmount}");
using (var conn = db.OpenConnection())
{
    var row = conn.QueryFirst<dynamic>("SELECT is_kot_only, report_visible, billed_at, bill_number, order_status FROM orders WHERE id = @id", new { id = bill.Id });
    Console.WriteLine($"  is_kot_only={row.is_kot_only}, report_visible={row.report_visible}, billed_at={row.billed_at}, order_status={row.order_status}");
}

// 4) Clear table
var clear = orders.SaveTableOrder(new TableOrderPayload { TableId = 5, TableStatus = "available", Items = new() });
Console.WriteLine($"\nClear: cleared={clear.Cleared}, tableStatus={clear.TableStatus}");
DumpTable(db, 5);

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
    Dump("BILL", b.BuildBill(items, "DR-0042", "5", 25, 335, stamp));

    var cfg58 = new Pos.Core.Printing.PrintConfig { PaperSize = "58mm", StoreName = "Chay Chaupal", ShowName = true };
    var b58 = new Pos.Core.Printing.ReceiptBuilder(cfg58);
    Console.WriteLine($"\n===== BILL 58mm ({b58.Cols} cols) =====");
    foreach (var l in b58.BuildBill(items, "DR-0042", "5", 0, 335, stamp).TrimEnd().Split('\n'))
        Console.WriteLine($"{l.TrimEnd('\r')}|{l.TrimEnd('\r').Length}");
}

static void DumpTable(DatabaseService db, long tableId)
{
    var view = new TableRepository(db).All().FirstOrDefault(t => t.Id == tableId);
    if (view != null)
        Console.WriteLine($"  table {view.TableNumber}: status={view.Status}, amount={view.Amount}");
}
