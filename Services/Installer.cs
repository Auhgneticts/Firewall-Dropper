using System.Diagnostics;
using System.Windows;
using FirewallDropper.Views;
using Microsoft.Win32;

namespace FirewallDropper.Services;

/// <summary>
/// Per-user install with no admin rights needed: copies the app to %LocalAppData%\Programs\Firewall Dropper,
/// adds Start menu, desktop and "Send to" shortcuts, and registers an entry under Settings › Apps.
/// The app also runs fine without being installed (e.g. straight from a USB stick).
/// </summary>
public static class Installer
{
    private const string ExeName = "FirewallDropper.exe";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FirewallDropper";
    private const string ShortcutDescription = "Drop programs here to add them to Windows Firewall";

    public static string InstallDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppInfo.Name);

    public static string InstalledExe { get; } = Path.Combine(InstallDir, ExeName);

    public static string DesktopShortcut { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppInfo.Name + ".lnk");

    public static string StartMenuShortcut { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppInfo.Name + ".lnk");

    public static string SendToShortcut { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SendTo), AppInfo.Name + ".lnk");

    public static bool IsInstalled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath);
            return key is not null && File.Exists(InstalledExe);
        }
    }

    public static bool IsRunningInstalledCopy => FirewallService.SamePath(AppInfo.ExePath, InstalledExe);

    /// <summary>The exe that shortcuts should point at: the installed copy if there is one, otherwise this one.</summary>
    public static string ShortcutTarget => IsInstalled ? InstalledExe : AppInfo.ExePath;

    public static bool HasDesktopShortcut => File.Exists(DesktopShortcut);
    public static bool HasSendToShortcut => File.Exists(SendToShortcut);

    public static void SetDesktopShortcut(bool on) => SetShortcut(DesktopShortcut, on);
    public static void SetSendToShortcut(bool on) => SetShortcut(SendToShortcut, on);

    private static void SetShortcut(string path, bool on)
    {
        if (on) ShellLink.Create(path, ShortcutTarget, ShortcutDescription);
        else if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Copies the app into the install folder and registers it. Throws on failure.</summary>
    public static void Install()
    {
        Directory.CreateDirectory(InstallDir);

        // A published build is one self-contained exe; a development build is a folder of files. Copy whatever is there.
        var sourceDir = AppContext.BaseDirectory;
        long totalBytes = 0;
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(InstallDir, Path.GetRelativePath(sourceDir, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            try
            {
                File.Copy(file, dest, overwrite: true);
            }
            catch (IOException)
            {
                throw new IOException($"Couldn't replace {Path.GetFileName(dest)}. If Firewall Dropper is already installed and open, close it and try again.");
            }
            totalBytes += new FileInfo(file).Length;
        }

        ShellLink.Create(StartMenuShortcut, InstalledExe, ShortcutDescription);
        ShellLink.Create(DesktopShortcut, InstalledExe, ShortcutDescription);
        ShellLink.Create(SendToShortcut, InstalledExe, ShortcutDescription);

        using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
        key.SetValue("DisplayName", AppInfo.Name);
        key.SetValue("DisplayVersion", AppInfo.Version);
        key.SetValue("Publisher", AppInfo.Name);
        key.SetValue("DisplayIcon", InstalledExe);
        key.SetValue("InstallLocation", InstallDir);
        key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)Math.Max(1, totalBytes / 1024), RegistryValueKind.DWord);
    }

    /// <summary>Interactive uninstall (FirewallDropper.exe --uninstall, or the App menu).</summary>
    public static void RunUninstall(Window? owner)
    {
        if (Dialog.Show(owner, $"Remove {AppInfo.Name} from this PC?",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        int ruleCount = 0;
        try { ruleCount = FirewallService.GetManagedRules().Count; } catch { }

        if (ruleCount > 0)
        {
            var answer = Dialog.Show(owner,
                $"{AppInfo.Name} created {ruleCount} firewall rule(s).\n\n" +
                "Yes: remove those rules too.\n" +
                "No: keep them in Windows Firewall (you can still delete them in Windows Defender Firewall, group \"Firewall Dropper\").",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel)
                return;
            if (answer == MessageBoxResult.Yes && RuleOperations.Run(RuleOperations.RemoveAll, [], owner) is null)
                Dialog.Show(owner, "Administrator permission was declined, so the firewall rules were kept.",
                    MessageBoxButton.OK, MessageBoxImage.Information);
        }

        foreach (var lnk in new[] { DesktopShortcut, StartMenuShortcut, SendToShortcut })
            TryDelete(lnk);
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false); } catch { }
        try { if (Directory.Exists(SettingsStore.Folder)) Directory.Delete(SettingsStore.Folder, true); } catch { }

        // The running exe can't delete itself, so a hidden cmd waits a moment and removes the folder.
        if (Directory.Exists(InstallDir))
        {
            Process.Start(new ProcessStartInfo("cmd.exe",
                $"/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{InstallDir}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetTempPath(),
            });
        }

        Dialog.Show(owner, $"{AppInfo.Name} was removed.", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
