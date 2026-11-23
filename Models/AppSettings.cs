using System.Text.Json.Serialization;

namespace FirewallDropper.Models;

/// <summary>What to do for one traffic direction when a program is dropped.</summary>
public enum RuleMode
{
    Block = 0,
    Allow = 1,
    None = 2,   // don't create a rule for this direction
}

/// <summary>What to do when a dropped program already has a Firewall Dropper rule.</summary>
public enum ExistingRuleBehavior
{
    Replace = 0,
    Skip = 1,
}

public sealed class AppSettings
{
    public RuleMode Inbound { get; set; } = RuleMode.Block;
    public RuleMode Outbound { get; set; } = RuleMode.Block;

    public bool ProfileDomain { get; set; } = true;
    public bool ProfilePrivate { get; set; } = true;
    public bool ProfilePublic { get; set; } = true;

    public ExistingRuleBehavior ExistingRules { get; set; } = ExistingRuleBehavior.Replace;

    /// <summary>Show the list of dropped programs and wait for OK. When off, rules are added right away and only results are shown.</summary>
    public bool ConfirmBeforeAdding { get; set; } = true;

    [JsonIgnore]
    public bool HasAnyDirection => Inbound != RuleMode.None || Outbound != RuleMode.None;

    [JsonIgnore]
    public bool HasAnyProfile => ProfileDomain || ProfilePrivate || ProfilePublic;

    [JsonIgnore]
    public bool IsValid => HasAnyDirection && HasAnyProfile;

    /// <summary>NET_FW_PROFILE2 bit mask for the selected profiles.</summary>
    [JsonIgnore]
    public int ProfileMask =>
        ProfileDomain && ProfilePrivate && ProfilePublic
            ? FirewallProfiles.All
            : (ProfileDomain ? FirewallProfiles.Domain : 0)
              | (ProfilePrivate ? FirewallProfiles.Private : 0)
              | (ProfilePublic ? FirewallProfiles.Public : 0);

    /// <summary>Human readable reason the settings can't be used, or null when valid.</summary>
    [JsonIgnore]
    public string? ValidationMessage =>
        !HasAnyDirection ? "Both directions are set to \"Don't add a rule\", so dropping a program would do nothing."
        : !HasAnyProfile ? "Select at least one network profile."
        : null;
}

public static class FirewallProfiles
{
    public const int Domain = 0x1;
    public const int Private = 0x2;
    public const int Public = 0x4;
    public const int All = 0x7FFFFFFF;

    public static string Describe(int mask)
    {
        if (mask == All || (mask & 0x7) == 0x7) return "All networks";
        var parts = new List<string>();
        if ((mask & Domain) != 0) parts.Add("Domain");
        if ((mask & Private) != 0) parts.Add("Private");
        if ((mask & Public) != 0) parts.Add("Public");
        return parts.Count == 0 ? "None" : string.Join(", ", parts);
    }
}
