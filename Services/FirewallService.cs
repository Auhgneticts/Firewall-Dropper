using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FirewallDropper.Models;

namespace FirewallDropper.Services;

public enum RuleDirection
{
    Inbound = 1,   // NET_FW_RULE_DIR_IN
    Outbound = 2,  // NET_FW_RULE_DIR_OUT
}

/// <summary>A Windows Firewall rule that Firewall Dropper created (tracked by its rule group).</summary>
public sealed class ManagedRule
{
    public required string Name { get; init; }
    public required string ProgramPath { get; init; }
    public required RuleDirection Direction { get; init; }
    public required RuleMode Action { get; init; }
    public required int Profiles { get; init; }
    public required bool Enabled { get; init; }
    public DateTime? Created { get; init; }
    public string? Source { get; init; }

    public string ProgramName => Path.GetFileName(ProgramPath);
    public string DirectionText => Direction.ToString();
    public string ActionText => Action == RuleMode.Allow ? "Allow" : "Block";
    public string ProfilesText => FirewallProfiles.Describe(Profiles);
    public string EnabledText => Enabled ? "On" : "Off";
    public string CreatedText => Created?.ToString("g", CultureInfo.CurrentCulture) ?? "";
    public bool ProgramExists => File.Exists(ProgramPath);
}

/// <summary>
/// Talks to Windows Defender Firewall through its COM API (HNetCfg.FwPolicy2).
/// Every rule this app creates is put in the rule group "Firewall Dropper", so the
/// firewall itself is the record of what the app manages. Nothing is stored elsewhere.
/// Reading works for normal users; adding, removing or changing rules needs administrator rights.
/// </summary>
public static class FirewallService
{
    public const string RuleGroup = AppInfo.Name;

    private const int ActionBlock = 0;  // NET_FW_ACTION_BLOCK
    private const int ActionAllow = 1;  // NET_FW_ACTION_ALLOW

    private static dynamic CreatePolicy() =>
        Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: true)!)!;

    private static dynamic CreateComRule() =>
        Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", throwOnError: true)!)!;

    public static List<ManagedRule> GetManagedRules()
    {
        var result = new List<ManagedRule>();
        dynamic policy = CreatePolicy();
        foreach (dynamic rule in policy.Rules)
        {
            string? group = rule.Grouping;
            if (!string.Equals(group, RuleGroup, StringComparison.OrdinalIgnoreCase))
                continue;

            string? description = rule.Description;
            result.Add(new ManagedRule
            {
                Name = (string)rule.Name,
                ProgramPath = (string?)rule.ApplicationName ?? "",
                Direction = (RuleDirection)(int)rule.Direction,
                Action = (int)rule.Action == ActionAllow ? RuleMode.Allow : RuleMode.Block,
                Profiles = (int)rule.Profiles,
                Enabled = (bool)rule.Enabled,
                Created = ParseCreated(description),
                Source = ParseSource(description),
            });
        }
        return result;
    }

    public static void AddRule(string programPath, RuleDirection direction, RuleMode action, int profiles, string source)
    {
        if (action == RuleMode.None)
            throw new ArgumentException("No rule to add.", nameof(action));

        dynamic rule = CreateComRule();
        rule.Name = BuildRuleName(programPath, direction);
        rule.Description = $"Created by {AppInfo.Name} on {DateTime.Now.ToString(CreatedFormat, CultureInfo.InvariantCulture)}. Source: {source}";
        rule.ApplicationName = programPath;
        rule.Direction = (int)direction;
        rule.Action = action == RuleMode.Allow ? ActionAllow : ActionBlock;
        rule.Profiles = profiles;
        rule.Grouping = RuleGroup;
        rule.InterfaceTypes = "All";
        rule.Enabled = true;

        dynamic policy = CreatePolicy();
        policy.Rules.Add(rule);
    }

    public static void RemoveRule(string name)
    {
        dynamic policy = CreatePolicy();
        policy.Rules.Remove(name);
    }

    public static void SetEnabled(string name, bool enabled)
    {
        dynamic policy = CreatePolicy();
        dynamic rule = policy.Rules.Item(name);
        rule.Enabled = enabled;
    }

    /// <summary>
    /// A readable, stable name. The short hash of the full path keeps names unique when
    /// two different programs share a file name (for example two copies of setup.exe).
    /// </summary>
    public static string BuildRuleName(string programPath, RuleDirection direction)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(programPath.ToUpperInvariant())))[..6];
        return $"{AppInfo.Name} - {Path.GetFileNameWithoutExtension(programPath)} ({direction}) [{hash}]";
    }

    public static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(a)),
                                 Path.GetFullPath(Environment.ExpandEnvironmentVariables(b)),
                                 StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Turns COM/permission failures into a sentence a user can act on.</summary>
    public static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Access denied. Administrator permission is required.",
        COMException com when (uint)com.HResult == 0x80070005 => "Access denied. Administrator permission is required.",
        COMException com when (uint)com.HResult == 0x80070002 => "The rule no longer exists.",
        _ => ex.Message,
    };

    private const string CreatedFormat = "yyyy-MM-dd HH:mm";
    private static readonly Regex CreatedRegex = new(@" on (\d{4}-\d{2}-\d{2} \d{2}:\d{2})\.", RegexOptions.Compiled);
    private static readonly Regex SourceRegex = new(@"Source: (.+)$", RegexOptions.Compiled);

    private static DateTime? ParseCreated(string? description)
    {
        var m = description is null ? null : CreatedRegex.Match(description);
        return m is { Success: true } &&
               DateTime.TryParseExact(m.Groups[1].Value, CreatedFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt
            : null;
    }

    private static string? ParseSource(string? description)
    {
        var m = description is null ? null : SourceRegex.Match(description);
        return m is { Success: true } ? m.Groups[1].Value : null;
    }
}
