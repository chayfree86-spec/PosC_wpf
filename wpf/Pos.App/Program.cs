namespace Pos.App;

/// <summary>
/// The real entry point, so Velopack's hook is the very first thing that runs.
///
/// On install, update and uninstall the Velopack bootstrapper relaunches the app with special
/// arguments; <c>VelopackApp.Build().Run()</c> handles those and exits before any WPF, database or
/// DI work would otherwise start. On a normal launch it returns immediately and the app starts as
/// usual.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Velopack.VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
