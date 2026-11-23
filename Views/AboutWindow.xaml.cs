using System.Diagnostics;
using System.Windows;
using FirewallDropper.Services;

namespace FirewallDropper.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {AppInfo.Version}" + (AppInfo.IsAdministrator ? " · running as administrator" : "");
        ExeText.Text = AppInfo.ExePath;
        ExeText.ToolTip = AppInfo.ExePath;
        SettingsText.Text = SettingsStore.FilePath;
        SettingsText.ToolTip = SettingsStore.FilePath;
        InstalledText.Text = Installer.IsInstalled ? $"Yes, in {Installer.InstallDir}" : "No (running standalone)";
    }

    private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SettingsStore.Folder);
        Process.Start(new ProcessStartInfo(SettingsStore.Folder) { UseShellExecute = true });
    }
}
