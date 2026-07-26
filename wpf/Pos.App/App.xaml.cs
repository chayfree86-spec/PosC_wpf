using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Pos.App.Services;
using Pos.App.ViewModels;
using Pos.Core.Data;
using Pos.Core.Repositories;
using Pos.Core.Sync;

namespace Pos.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private static readonly string ErrorLog =
        Path.Combine(Path.GetTempPath(), "pos_app_error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            // Handled, so the till stays open. An error while saving or printing one bill
            // used to close the whole app mid-service — the counter would be staring at a
            // desktop with a customer waiting. Tell the operator what went wrong and let
            // them try again instead.
            File.AppendAllText(ErrorLog,
                $"{DateTime.UtcNow.AddMinutes(330):yyyy-MM-dd HH:mm:ss} Dispatcher: {args.Exception}"
                + Environment.NewLine + Environment.NewLine);
            MessageBox.Show(args.Exception.Message, "POS error", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            File.WriteAllText(ErrorLog, "Domain: " + args.ExceptionObject);

        try
        {
            Start(e);
        }
        catch (Exception ex)
        {
            File.WriteAllText(ErrorLog, "Startup: " + ex);
            MessageBox.Show(ex.ToString(), "POS startup error");
            Shutdown();
        }
    }

    private void Start(StartupEventArgs e)
    {
        base.OnStartup(e);

        var sc = new ServiceCollection();

        // The canonical local database: Documents\ChayChaupalPOS\sqlite\pos.sqlite3.
        // Billing runs entirely against this file — nothing on screen needs the network.
        sc.AddSingleton(new DatabaseService(DatabaseService.DefaultDbPath()));
        sc.AddSingleton<MenuRepository>();
        sc.AddSingleton<TableRepository>();
        sc.AddSingleton<OrderRepository>();
        sc.AddSingleton<CustomerLedgerRepository>();
        sc.AddSingleton<QuickNotesRepository>();
        sc.AddSingleton<AppSettingsRepository>();
        sc.AddSingleton<AuthRepository>();
        sc.AddSingleton<CatalogRepository>();
        sc.AddSingleton<ReportsRepository>();
        sc.AddSingleton<SyncCoordinator>();
        sc.AddSingleton<ReportsViewModel>();
        sc.AddSingleton<NotesViewModel>();
        sc.AddSingleton<QrOrderViewModel>();
        sc.AddSingleton<LedgerViewModel>();
        sc.AddSingleton<SettingsViewModel>();
        sc.AddSingleton<MainViewModel>();

        Services = sc.BuildServiceProvider();

        // Create schema (idempotent) and seed sample data on first run so the
        // screen has something to show before the API bootstrap sync exists.
        var db = Services.GetRequiredService<DatabaseService>();
        new SqliteMigrationRunner(db).Migrate();
        SampleSeeder.SeedIfEmpty(db);

        // Bills are written to SQLite first and pushed to the server in the background, so
        // the till keeps working whether or not the network does.
        var sync = Services.GetRequiredService<SyncCoordinator>();
        sync.Start();

        // A saved setting goes out at once instead of waiting for the next scheduled pass —
        // the operator has just pressed Save and expects it to be shared, not queued.
        Services.GetRequiredService<AppSettingsRepository>().SettingQueued += sync.NudgePush;

        var window = new MainWindow { DataContext = Services.GetRequiredService<MainViewModel>() };
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
        window.Activate();
        window.Focus();
    }
}
