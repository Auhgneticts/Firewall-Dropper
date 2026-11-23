using System.Diagnostics;

namespace FirewallDropper.Services;

/// <summary>The result of turning a dropped file into the program (.exe) a firewall rule should target.</summary>
public sealed record ResolvedProgram(string Source, string? ExePath, string DisplayName, string? Error)
{
    public bool IsValid => Error is null && ExePath is not null;
}

/// <summary>Follows shortcuts to the real program and explains clearly when that's not possible.</summary>
public static class ProgramResolver
{
    private static readonly HashSet<string> SharedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "mmc.exe", "rundll32.exe", "explorer.exe", "dllhost.exe", "control.exe",
    };

    public static ResolvedProgram Resolve(string source)
    {
        string name = SafeName(source);
        ResolvedProgram Fail(string error) => new(source, null, name, error);

        try
        {
            if (Directory.Exists(source))
                return Fail("This is a folder. Drop the program (.exe) or its shortcut instead.");
            if (!File.Exists(source))
                return Fail("The file could not be found.");

            string target = source;
            switch (Path.GetExtension(source).ToLowerInvariant())
            {
                case ".lnk":
                    var resolved = ShellLink.GetAdvertisedTarget(source) ?? ShellLink.GetTarget(source);
                    if (resolved is null)
                        return Fail("This shortcut doesn't point to a program file. It is probably a Microsoft Store app or a special Windows shortcut, and those can't be added to the firewall this way.");
                    target = resolved;
                    break;

                case ".url":
                    return Fail("This is an internet shortcut (.url), such as a web link or game launcher link, not a program. Drop the program's .exe instead.");

                case ".appref-ms":
                    return Fail("ClickOnce application shortcuts aren't supported. Drop the program's .exe instead.");
            }

            if (Directory.Exists(target))
                return Fail($"The shortcut points to a folder, not a program: {target}");
            if (!File.Exists(target))
                return Fail($"The shortcut's program was not found: {target}");
            if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return Fail($"Only programs (.exe) can be added. This points to a {Path.GetExtension(target)} file.");

            target = Path.GetFullPath(target);
            if (string.Equals(target, AppInfo.ExePath, StringComparison.OrdinalIgnoreCase))
                return Fail("That's Firewall Dropper itself.");

            // Some shortcuts start a shared Windows host (e.g. Hyper-V Manager runs mmc.exe). A rule on the host
            // would affect every tool that uses it, which is almost never what was meant.
            if (!string.Equals(target, source, StringComparison.OrdinalIgnoreCase)
                && SharedHosts.Contains(Path.GetFileName(target)))
                return Fail($"This shortcut starts {Path.GetFileName(target)}, a Windows program shared by many tools. " +
                            "A rule on it would affect all of them. If you really mean it, drop the .exe itself.");

            return new ResolvedProgram(source, target, DisplayNameFor(source, target), null);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    /// <summary>Resolves a batch and drops duplicates (e.g. a program and its shortcut dropped together).</summary>
    public static List<ResolvedProgram> ResolveAll(IEnumerable<string> sources)
    {
        var list = new List<ResolvedProgram>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var src in sources.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            var r = Resolve(src);
            if (r.ExePath is not null && !seen.Add(r.ExePath))
                continue;
            list.Add(r);
        }
        return list;
    }

    private static string DisplayNameFor(string source, string exe)
    {
        if (source.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return Path.GetFileNameWithoutExtension(source);
        try
        {
            var desc = FileVersionInfo.GetVersionInfo(exe).FileDescription;
            if (!string.IsNullOrWhiteSpace(desc)) return desc.Trim();
        }
        catch { }
        return Path.GetFileNameWithoutExtension(exe);
    }

    private static string SafeName(string path)
    {
        try { return Path.GetFileNameWithoutExtension(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path; }
        catch { return path; }
    }
}
