using System.Windows;
using System.Windows.Threading;
using FirewallDropper.Services;
using FirewallDropper.Views;

namespace FirewallDropper;

/// <summary>
/// How the app starts:
///   FirewallDropper.exe                      opens the settings window
///   FirewallDropper.exe file1 file2 ...      files were dropped on the icon or sent via "Send to": shows the confirm dialog
///   FirewallDropper.exe --apply file1 ...    elevated copy started by the dialog: applies straight away and shows results
///   FirewallDropper.exe --rules op names...  elevated helper used by the rules list
///   FirewallDropper.exe --uninstall          used by Settings › Apps
/// Elevated copies may get a leading "--center x,y" so their windows open over the window that started them.
/// Only one normal copy runs at a time (see SingleInstance); the elevated helper copies are exempt.
/// </summary>
public partial class App : Application
{
    public const string ApplyFlag = "--apply";
    public const string UninstallFlag = "--uninstall";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        string[] args = WindowPlacement.ConsumeArgs(e.Args);
        string first = args.Length > 0 ? args[0].ToLowerInvariant() : "";

        switch (first)
        {
            case RuleOperations.HelperFlag:
                // Only dialogs are shown here; don't let closing one end the process early.
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Shutdown(RuleOperations.RunHelper(args[1..]));
                return;

            case UninstallFlag:
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Installer.RunUninstall(owner: null);
                Shutdown();
                return;

            case ApplyFlag:
                var results = new DropWindow(args[1..], applyImmediately: true);
                WindowPlacement.Place(results, owner: null);
                results.Show();
                return;
        }

        // Normal launch or dropped files: only one copy may run. Hand the request to it if it's already open.
        if (!SingleInstance.TryBecomePrimary())
        {
            if (SingleInstance.SendToPrimary(args))
            {
                Shutdown();
                return;
            }
            // The running copy didn't answer (it may be closing), so carry on as a normal launch.
        }
        else
        {
            SingleInstance.StartServer(OnRequestFromAnotherLaunch);
        }

        if (args.Length == 0)
            ShowMainWindow(owner: null);
        else if (!DropWindow.HandleDrop(args, owner: null))
            Shutdown(); // an elevated copy took over and shows the results
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SingleInstance.Release();
        base.OnExit(e);
    }

    /// <summary>Someone launched the app again while it was running: show the settings window, or handle their dropped files.</summary>
    private static void OnRequestFromAnotherLaunch(string[]? files)
    {
        if (files is null || files.Length == 0)
        {
            ShowMainWindow(owner: null);
            return;
        }

        // If this copy runs as administrator, never add rules without the user clicking OK:
        // the request came from an ordinary (non-elevated) launch.
        DropWindow.HandleDrop(files, WindowPlacement.ActiveWindow(), forceConfirm: AppInfo.IsAdministrator);
    }

    /// <summary>Shows the one settings window, creating it if needed, and brings it to the front.</summary>
    public static MainWindow ShowMainWindow(Window? owner)
    {
        var main = Current.Windows.OfType<MainWindow>().FirstOrDefault();
        if (main is null)
        {
            main = new MainWindow();
            if (owner is not null) WindowPlacement.Place(main, owner);
            main.Show();
            main.Owner = null; // centered on the caller, but not tied to it (closing the caller mustn't close settings)
        }
        BringToFront(main);
        return main;
    }

    public static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
        // Nudge Windows into actually raising it when the request came from another process.
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        try
        {
            Dialog.Show(null, e.Exception.Message, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            MessageBox.Show(e.Exception.Message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
