using System.Windows;

namespace FirewallDropper.Services;

/// <summary>
/// Remove / enable / disable tracked rules. When the app isn't running as administrator,
/// the work is handed to an elevated copy of the app: FirewallDropper.exe --rules &lt;op&gt; &lt;names...&gt;.
/// </summary>
public static class RuleOperations
{
    public const string HelperFlag = "--rules";
    public const string Remove = "remove";
    public const string Enable = "enable";
    public const string Disable = "disable";
    public const string RemoveAll = "remove-all";

    /// <summary>Runs the operation. Returns true on success, false on error, null if the user declined the UAC prompt.</summary>
    public static async Task<bool?> RunAsync(string op, IReadOnlyList<string> names)
    {
        if (AppInfo.IsAdministrator)
        {
            var errors = await Task.Run(() => Perform(op, names));
            ShowErrors(errors);
            return errors.Count == 0;
        }

        var proc = Elevation.RelaunchElevated([HelperFlag, op, .. names]);
        if (proc is null)
            return null;
        await proc.WaitForExitAsync();
        return proc.ExitCode == 0;
    }

    /// <summary>Blocking version, used during uninstall.</summary>
    public static bool? Run(string op, IReadOnlyList<string> names)
    {
        if (AppInfo.IsAdministrator)
        {
            var errors = Perform(op, names);
            ShowErrors(errors);
            return errors.Count == 0;
        }

        var proc = Elevation.RelaunchElevated([HelperFlag, op, .. names]);
        if (proc is null)
            return null;
        proc.WaitForExit();
        return proc.ExitCode == 0;
    }

    /// <summary>Entry point for the elevated helper process.</summary>
    public static int RunHelper(string[] args)
    {
        if (args.Length == 0)
            return 2;
        var errors = Perform(args[0], args.Skip(1).ToList());
        ShowErrors(errors);
        return errors.Count == 0 ? 0 : 1;
    }

    private static List<string> Perform(string op, IReadOnlyList<string> names)
    {
        var errors = new List<string>();
        try
        {
            if (op == RemoveAll)
                names = FirewallService.GetManagedRules().Select(r => r.Name).ToList();

            foreach (var name in names)
            {
                try
                {
                    switch (op)
                    {
                        case Remove:
                        case RemoveAll: FirewallService.RemoveRule(name); break;
                        case Enable: FirewallService.SetEnabled(name, true); break;
                        case Disable: FirewallService.SetEnabled(name, false); break;
                        default: errors.Add($"Unknown operation '{op}'."); return errors;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{name}: {FirewallService.Describe(ex)}");
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add(FirewallService.Describe(ex));
        }
        return errors;
    }

    private static void ShowErrors(List<string> errors)
    {
        if (errors.Count == 0) return;
        MessageBox.Show("Some rules could not be changed:\n\n" + string.Join("\n", errors.Take(15)),
            AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
