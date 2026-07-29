using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Pos.App.Services;
using Pos.App.ViewModels;
using Pos.App.Views;
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
            Alert(args.Exception.Message, "POS error");
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
            Alert(ex.ToString(), "POS startup error");
            Shutdown();
        }
    }

    /// <summary>
    /// Points the whole app at the business the signed-in operator belongs to.
    ///
    /// Called after every sign-in, including the one after a logout: the counter is shared, so
    /// the brand can change without the app restarting. A till whose staff list has never
    /// synced signs nobody in and simply keeps the client it already had.
    /// </summary>
    public static void ApplySignedInClient()
    {
        if (Session.User is not { } user)
        {
            return;
        }

        Services.GetRequiredService<ClientContext>()
            .Use(user.ClientId, user.ClientSlug, user.ClientName);
    }

    /// <summary>
    /// The crash handlers' way of speaking to the operator.
    ///
    /// Themed like every other dialog in the app, but with the native box behind it: these two
    /// handlers are the last thing standing between a fault and a blank screen, and a startup
    /// that failed early enough to take the app resources with it would otherwise swallow the
    /// one message explaining why nothing opened.
    /// </summary>
    private static void Alert(string message, string title)
    {
        try
        {
            ThemeMessageBox.Show(message, title, "error");
        }
        catch
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Start(StartupEventArgs e)
    {
        base.OnStartup(e);

        var sc = new ServiceCollection();

        // The canonical local database: Documents\ChayChaupalPOS\sqlite\pos.sqlite3.
        // Billing runs entirely against this file — nothing on screen needs the network.
        sc.AddSingleton(new DatabaseService(DatabaseService.DefaultDbPath()));

        // Which business the counter is billing for. Set from the sign-in below and read by
        // every repository, so a Chay Chaupal shift can't file its takings under Daal Roti.
        sc.AddSingleton<ClientContext>();
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

        // Nobody bills until the till knows who is standing at it.
        //
        // Skipped on a machine whose staff list has never come down: there is nothing to check
        // a PIN against before the first sync, and locking a fresh install out of its own app
        // would strand the counter on the one day it can least afford it.
        if (Services.GetRequiredService<AuthRepository>().HasUsers() && !LoginWindow.Authenticate())
        {
            Shutdown();
            return;
        }

        // Every bill from here on records who took it, so Reports can be read per operator.
        // Read through a delegate rather than captured once: the operator changes at logout,
        // and a captured id would keep crediting whoever opened the app this morning.
        Services.GetRequiredService<OrderRepository>().CurrentUserId = () => Session.User?.Id;
        ApplySignedInClient();

        var window = new MainWindow { DataContext = Services.GetRequiredService<MainViewModel>() };
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
        window.Activate();
        window.Focus();
    }
}
