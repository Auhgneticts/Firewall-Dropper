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
/// </summary>
public partial class App : Application
{
    public const string ApplyFlag = "--apply";
    public const string UninstallFlag = "--uninstall";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        string[] args = e.Args;
        string first = args.Length > 0 ? args[0].ToLowerInvariant() : "";

        switch (first)
        {
            case RuleOperations.HelperFlag:
                Shutdown(RuleOperations.RunHelper(args[1..]));
                return;

            case UninstallFlag:
                Installer.RunUninstall();
                Shutdown();
                return;

            case ApplyFlag:
                new DropWindow(args[1..], applyImmediately: true).Show();
                return;

            case "":
                new MainWindow().Show();
                return;

            default:
                if (!DropWindow.HandleDrop(args, owner: null))
                    Shutdown(); // an elevated copy took over and shows the results
                return;
        }
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
