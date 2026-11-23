using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using FirewallDropper.Models;
using FirewallDropper.Services;

namespace FirewallDropper.Views;

/// <summary>The settings window shown when the app is opened normally.</summary>
public partial class MainWindow : Window
{
    private AppSettings _settings = SettingsStore.Load();
    private bool _loading = true;

    public MainWindow()
    {
        InitializeComponent();
        LoadSettingsIntoControls();
        RefreshShortcutTip();
        Activated += async (_, _) =>
        {
            // The drop dialog can change settings too, so pick up the latest when coming back here.
            _settings = SettingsStore.Load();
            LoadSettingsIntoControls();
            await RefreshRuleCountAsync();
        };
    }

    private void LoadSettingsIntoControls()
    {
        _loading = true;
        InboundBox.SelectedIndex = (int)_settings.Inbound;
        OutboundBox.SelectedIndex = (int)_settings.Outbound;
        DomainBox.IsChecked = _settings.ProfileDomain;
        PrivateBox.IsChecked = _settings.ProfilePrivate;
        PublicBox.IsChecked = _settings.ProfilePublic;
        ReplaceRadio.IsChecked = _settings.ExistingRules == ExistingRuleBehavior.Replace;
        SkipRadio.IsChecked = _settings.ExistingRules == ExistingRuleBehavior.Skip;
        ConfirmBox.IsChecked = _settings.ConfirmBeforeAdding;
        _loading = false;
        UpdateValidation();
    }

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        _settings.Inbound = (RuleMode)Math.Max(0, InboundBox.SelectedIndex);
        _settings.Outbound = (RuleMode)Math.Max(0, OutboundBox.SelectedIndex);
        _settings.ProfileDomain = DomainBox.IsChecked == true;
        _settings.ProfilePrivate = PrivateBox.IsChecked == true;
        _settings.ProfilePublic = PublicBox.IsChecked == true;
        _settings.ExistingRules = SkipRadio.IsChecked == true ? ExistingRuleBehavior.Skip : ExistingRuleBehavior.Replace;
        _settings.ConfirmBeforeAdding = ConfirmBox.IsChecked == true;

        try
        {
            SettingsStore.Save(_settings);
            SavedText.Text = "Changes are saved automatically.";
        }
        catch (Exception ex)
        {
            SavedText.Text = "Couldn't save settings: " + ex.Message;
        }
        UpdateValidation();
    }

    private void UpdateValidation()
    {
        var message = _settings.ValidationMessage;
        ValidationText.Text = message ?? "";
        ValidationCard.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshShortcutTip() =>
        ShortcutTip.Visibility = Installer.HasDesktopShortcut ? Visibility.Collapsed : Visibility.Visible;

    private async Task RefreshRuleCountAsync()
    {
        try
        {
            var rules = await Task.Run(FirewallService.GetManagedRules);
            int programs = rules.Select(r => r.ProgramPath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            RuleCountRun.Text = rules.Count == 0
                ? "No programs added yet ·"
                : $"{rules.Count} rule{(rules.Count == 1 ? "" : "s")} for {programs} program{(programs == 1 ? "" : "s")} ·";
        }
        catch
        {
            RuleCountRun.Text = "";
        }
    }

    // ----- Drop zone -----

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        bool ok = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = ok ? DragDropEffects.Link : DragDropEffects.None;
        if (ok) DropZoneBorder.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentFillColorDefaultBrush");
        e.Handled = true;
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e) =>
        DropZoneBorder.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ControlStrongStrokeColorDefaultBrush");

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        DropZone_DragLeave(sender, e);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            DropWindow.HandleDrop(files, this);
    }

    // ----- App menu -----

    private void AppMenu_Opened(object sender, RoutedEventArgs e)
    {
        bool installed = Installer.IsInstalled;
        MenuInstall.Visibility = Installer.IsRunningInstalledCopy ? Visibility.Collapsed : Visibility.Visible;
        MenuInstall.Header = installed ? "Re_install / update on this PC…" : "_Install on this PC…";
        MenuUninstall.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        MenuDesktop.IsChecked = Installer.HasDesktopShortcut;
        MenuSendTo.IsChecked = Installer.HasSendToShortcut;
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        var answer = Dialog.Show(this,
            $"Install {AppInfo.Name} for your user account?\n\n" +
            $"• Copies the app to {Installer.InstallDir}\n" +
            "• Adds Start menu and desktop shortcuts\n" +
            "• Adds it to the right-click “Send to” menu\n" +
            "• Lists it in Settings › Apps so it can be uninstalled\n\n" +
            "No administrator permission is needed.",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        try
        {
            Installer.Install();
        }
        catch (Exception ex)
        {
            Dialog.Show(this, "Install failed: " + ex.Message, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RefreshShortcutTip();
        if (Dialog.Show(this, "Installed. Open the installed copy now?",
                MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
        {
            // Step aside first, or the new copy would just hand its launch back to this closing one.
            SingleInstance.Release();
            Process.Start(new ProcessStartInfo(Installer.InstalledExe) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        Installer.RunUninstall(this);
        if (!Installer.IsInstalled && Installer.IsRunningInstalledCopy)
            Application.Current.Shutdown();
        RefreshShortcutTip();
    }

    private void DesktopShortcut_Click(object sender, RoutedEventArgs e) =>
        ToggleShortcut(() => Installer.SetDesktopShortcut(MenuDesktop.IsChecked));

    private void SendTo_Click(object sender, RoutedEventArgs e) =>
        ToggleShortcut(() => Installer.SetSendToShortcut(MenuSendTo.IsChecked));

    private void CreateDesktopShortcut_Click(object sender, RoutedEventArgs e) =>
        ToggleShortcut(() => Installer.SetDesktopShortcut(true));

    private void ToggleShortcut(Action action)
    {
        try { action(); }
        catch (Exception ex) { Dialog.Show(this, ex.Message, MessageBoxButton.OK, MessageBoxImage.Error); }
        RefreshShortcutTip();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    // ----- Rules / Help menus -----

    private void ViewRules_Click(object sender, RoutedEventArgs e) => RulesWindow.ShowSingle(this);

    private void OpenWindowsFirewall_Click(object sender, RoutedEventArgs e) => OpenWindowsFirewall(this);

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow();
        WindowPlacement.Place(about, this);
        about.ShowDialog();
    }

    public static void OpenWindowsFirewall(Window owner)
    {
        try
        {
            Process.Start(new ProcessStartInfo("wf.msc") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Declining the UAC prompt for the console lands here; nothing to report in that case.
            if (ex is not System.ComponentModel.Win32Exception { NativeErrorCode: 1223 })
                Dialog.Show(owner, ex.Message, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
