using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace FirewallDropper.Services;

/// <summary>Starts another copy of this app with administrator rights (UAC prompt).</summary>
public static class Elevation
{
    private const int ErrorCancelled = 1223;

    /// <summary>Returns the started process, or null if the user declined the UAC prompt.</summary>
    public static Process? RelaunchElevated(IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(AppInfo.ExePath)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = JoinArguments(args),
        };

        try
        {
            return Process.Start(psi);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return null;
        }
    }

    /// <summary>Builds a command line using the standard Windows (CommandLineToArgvW) quoting rules.</summary>
    public static string JoinArguments(IEnumerable<string> args) => string.Join(" ", args.Select(Quote));

    private static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0)
            return arg;

        var sb = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            if (c == '"')
                sb.Append('\\', backslashes * 2 + 1);
            else
                sb.Append('\\', backslashes);
            backslashes = 0;
            sb.Append(c);
        }
        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }
}
