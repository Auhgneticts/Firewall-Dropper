using FirewallDropper.Models;

namespace FirewallDropper.Services;

public enum ApplyStatus
{
    Done,
    Partial,
    Skipped,
    Failed,
}

public sealed record ApplyOutcome(ApplyStatus Status, string Message);

/// <summary>Applies the user's settings to one dropped program. Needs administrator rights.</summary>
public static class DropProcessor
{
    /// <param name="existing">Current Firewall Dropper rules. Updated in place as rules are replaced or added.</param>
    public static ApplyOutcome Apply(ResolvedProgram program, AppSettings settings, List<ManagedRule> existing)
    {
        if (!program.IsValid)
            return new ApplyOutcome(ApplyStatus.Failed, program.Error ?? "Not a program.");

        string exe = program.ExePath!;
        var messages = new List<string>();
        int added = 0, skipped = 0, failed = 0;

        foreach (var (direction, mode) in new[] { (RuleDirection.Inbound, settings.Inbound), (RuleDirection.Outbound, settings.Outbound) })
        {
            if (mode == RuleMode.None)
                continue;

            try
            {
                var matches = existing.Where(r => r.Direction == direction && FirewallService.SamePath(r.ProgramPath, exe)).ToList();
                if (matches.Count > 0 && settings.ExistingRules == ExistingRuleBehavior.Skip)
                {
                    messages.Add($"{direction}: already has a rule, kept it");
                    skipped++;
                    continue;
                }

                foreach (var old in matches)
                {
                    FirewallService.RemoveRule(old.Name);
                    existing.Remove(old);
                }

                FirewallService.AddRule(exe, direction, mode, settings.ProfileMask, program.Source);
                existing.Add(new ManagedRule
                {
                    Name = FirewallService.BuildRuleName(exe, direction),
                    ProgramPath = exe,
                    Direction = direction,
                    Action = mode,
                    Profiles = settings.ProfileMask,
                    Enabled = true,
                    Created = DateTime.Now,
                    Source = program.Source,
                });

                string verb = mode == RuleMode.Allow ? "allowed" : "blocked";
                messages.Add(matches.Count > 0 ? $"{direction} {verb} (replaced old rule)" : $"{direction} {verb}");
                added++;
            }
            catch (Exception ex)
            {
                messages.Add($"{direction}: {FirewallService.Describe(ex)}");
                failed++;
            }
        }

        var status = failed > 0 ? (added > 0 ? ApplyStatus.Partial : ApplyStatus.Failed)
                   : added == 0 && skipped > 0 ? ApplyStatus.Skipped
                   : ApplyStatus.Done;
        return new ApplyOutcome(status, string.Join("  ·  ", messages));
    }
}
