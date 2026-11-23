using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FirewallDropper.Models;
using FirewallDropper.Services;

namespace FirewallDropper.Views;

/// <summary>
/// The small dialog shown when files are dropped onto the app's icon (or "Send to", or the drop zone).
/// It lists what will happen, waits for OK, then applies the rules and shows per-program results and errors.
/// </summary>
public partial class DropWindow : Window
{
    private readonly bool _applyImmediately;
    private readonly ObservableCollection<DropItem> _items = new();
    private AppSettings _settings = new();
    private bool _finished;

    public DropWindow(IEnumerable<string> sources, bool applyImmediately)
    {
        InitializeComponent();
        _applyImmediately = applyImmediately;
        ItemsList.ItemsSource = _items;

        foreach (var program in ProgramResolver.ResolveAll(sources))
            _items.Add(new DropItem(program));

        ReloadSettings();
        Loaded += OnLoaded;
        Activated += OnActivated;
    }

    /// <summary>
    /// Entry point for dropped files. Shows the confirmation list, or when the user turned confirmation off,
    /// goes straight to adding the rules (elevating if needed) and shows only the results.
    /// Returns false when no window was opened in this process (the work moved to an elevated copy).
    /// <paramref name="forceConfirm"/> always shows the list first, whatever the setting says.
    /// </summary>
    public static bool HandleDrop(IReadOnlyList<string> sources, Window? owner, bool forceConfirm = false)
    {
        var settings = SettingsStore.Load();
        bool skipConfirm = !forceConfirm && !settings.ConfirmBeforeAdding && settings.IsValid
                           && ProgramResolver.ResolveAll(sources).Any(p => p.IsValid);

        DropWindow window;
        if (!skipConfirm)
        {
            window = new DropWindow(sources, applyImmediately: false);
        }
        else if (AppInfo.IsAdministrator)
        {
            window = new DropWindow(sources, applyImmediately: true);
        }
        else if (Elevation.RelaunchElevated([App.ApplyFlag, .. sources], owner) is not null)
        {
            return false;
        }
        else
        {
            // UAC was declined: show the list so the user can retry with OK.
            window = new DropWindow(sources, applyImmediately: false);
            window.ShowMessage(MessageKind.Warning, "Administrator permission was declined, so no changes were made. Click OK to try again.");
        }

        WindowPlacement.Place(window, owner);
        window.Show();
        App.BringToFront(window); // may have been requested by another launch, so make sure it's on top
        return true;
    }

    private IEnumerable<DropItem> ValidItems => _items.Where(i => i.Program.IsValid);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_applyImmediately)
        {
            if (AppInfo.IsAdministrator)
                await ApplyAsync();
            else
                ShowMessage(MessageKind.Error, "Firewall Dropper needs administrator permission to change the firewall.");
        }
    }

    private void ReloadSettings()
    {
        _settingsStamp = File.Exists(SettingsStore.FilePath) ? File.GetLastWriteTimeUtc(SettingsStore.FilePath) : DateTime.MinValue;
        _settings = SettingsStore.Load();

        ChipPanel.Children.Clear();
        AddChip("", "Inbound: " + Describe(_settings.Inbound));
        AddChip("", "Outbound: " + Describe(_settings.Outbound));
        AddChip("", FirewallProfiles.Describe(_settings.ProfileMask));
        AddChip("", _settings.ExistingRules == ExistingRuleBehavior.Replace ? "Replace existing rules" : "Keep existing rules");
        AskFirstBox.IsChecked = _settings.ConfirmBeforeAdding;

        UpdateHeaderAndButtons();
    }

    private static string Describe(RuleMode mode) => mode switch
    {
        RuleMode.Block => "Block",
        RuleMode.Allow => "Allow",
        _ => "no rule",
    };

    private void AddChip(string glyph, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = glyph, Style = (Style)FindResource("Glyph"), FontSize = 12, Margin = new Thickness(0, 0, 6, 0) });
        panel.Children.Add(new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        var chip = new Border
        {
            Child = panel,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 6, 4),
        };
        chip.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");
        ChipPanel.Children.Add(chip);
    }

    private void UpdateHeaderAndButtons()
    {
        if (_finished) return;

        int valid = ValidItems.Count();
        int invalid = _items.Count - valid;

        TitleText.Text = valid switch
        {
            0 => "Nothing that can be added",
            1 => "Add 1 program to Windows Firewall?",
            _ => $"Add {valid} programs to Windows Firewall?",
        };
        SubtitleText.Text = invalid == 0
            ? "Check the list below, then click OK."
            : $"{invalid} item{(invalid == 1 ? "" : "s")} can't be added. See the details below.";

        if (!_settings.IsValid)
            ShowMessage(MessageKind.Error, _settings.ValidationMessage + " Use Change settings to fix this.");
        else
            HideMessage();

        OkButton.IsEnabled = valid > 0 && _settings.IsValid;
        ShieldHint.Visibility = OkButton.IsEnabled && !AppInfo.IsAdministrator && !_applyImmediately
            ? Visibility.Visible : Visibility.Collapsed;
        if (valid == 0) OkButton.Content = "OK";
    }

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_finished)
        {
            Close();
            return;
        }

        if (AppInfo.IsAdministrator)
        {
            await ApplyAsync();
            return;
        }

        // Hand the work to an elevated copy of the app, which shows its own results window.
        var sources = ValidItems.Select(i => i.Program.Source).ToList();
        if (Elevation.RelaunchElevated([App.ApplyFlag, .. sources], this) is null)
        {
            ShowMessage(MessageKind.Warning, "Administrator permission was declined, so no changes were made. Click OK to try again.");
            return;
        }
        Close();
    }

    private async Task ApplyAsync()
    {
        var work = ValidItems.ToList();
        if (work.Count == 0 || !_settings.IsValid) return;

        OkButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        ShieldHint.Visibility = Visibility.Collapsed;
        Busy.Visibility = Visibility.Visible;
        HideMessage();
        foreach (var item in work) item.SetWorking();

        try
        {
            var existing = await Task.Run(FirewallService.GetManagedRules);
            foreach (var item in work)
            {
                var outcome = await Task.Run(() => DropProcessor.Apply(item.Program, _settings, existing));
                item.SetOutcome(outcome);
            }
        }
        catch (Exception ex)
        {
            ShowMessage(MessageKind.Error, "Windows Firewall couldn't be changed: " + FirewallService.Describe(ex));
            foreach (var item in work.Where(i => i.State == DropItemState.Working))
                item.SetOutcome(new ApplyOutcome(ApplyStatus.Failed, "Not added."));
        }

        Busy.Visibility = Visibility.Collapsed;
        ShowSummary();
    }

    private void ShowSummary()
    {
        _finished = true;
        int done = _items.Count(i => i.State == DropItemState.Done);
        int warn = _items.Count(i => i.State == DropItemState.Warning);
        int bad = _items.Count(i => i.State is DropItemState.Failed or DropItemState.Invalid);

        TitleText.Text = bad == 0 && warn == 0 ? "Done" : "Finished with some problems";
        var parts = new List<string>();
        if (done > 0) parts.Add($"{done} added");
        if (warn > 0) parts.Add($"{warn} partly added or skipped");
        if (bad > 0) parts.Add($"{bad} not added");
        SubtitleText.Text = string.Join(" · ", parts);

        // Past tense now that the work is done. If anything was skipped or failed, not every rule was created.
        ChipCaption.Text = bad == 0 && warn == 0 ? "Rules that were created" : "Rules that were requested";
        ChangeSettingsLink.Visibility = Visibility.Collapsed;

        OkButton.Content = "Close";
        OkButton.IsEnabled = true;
        CancelButton.Visibility = Visibility.Collapsed;
        ViewRulesButton.Visibility = Visibility.Visible;
        OkButton.Focus();
    }

    // IsCancel only auto-closes modal dialogs; this window is shown modeless, so close it explicitly.
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void AskFirst_Click(object sender, RoutedEventArgs e)
    {
        bool askFirst = AskFirstBox.IsChecked == true;
        _settings.ConfirmBeforeAdding = askFirst;
        try
        {
            var saved = SettingsStore.Load();
            saved.ConfirmBeforeAdding = askFirst;
            SettingsStore.Save(saved);
            _settingsStamp = File.GetLastWriteTimeUtc(SettingsStore.FilePath); // our own change; no reload needed
        }
        catch (Exception ex)
        {
            ShowMessage(MessageKind.Error, "Couldn't save the setting: " + ex.Message);
        }
    }

    private void ChangeSettings_Click(object sender, RoutedEventArgs e)
    {
        // One settings window for the whole app; this dialog picks up changes when it's activated again.
        App.ShowMainWindow(this);
    }

    private DateTime _settingsStamp;

    private void OnActivated(object? sender, EventArgs e)
    {
        if (_finished) return;
        var stamp = File.Exists(SettingsStore.FilePath) ? File.GetLastWriteTimeUtc(SettingsStore.FilePath) : DateTime.MinValue;
        if (stamp != _settingsStamp)
            ReloadSettings();
    }

    private void ViewRules_Click(object sender, RoutedEventArgs e) => RulesWindow.ShowSingle(this);

    // ----- Message bar -----

    private enum MessageKind { Info, Warning, Error }

    private void ShowMessage(MessageKind kind, string text)
    {
        (string glyph, string brush, string background) = kind switch
        {
            MessageKind.Error => ("", "SystemFillColorCriticalBrush", "SystemFillColorCriticalBackgroundBrush"),
            MessageKind.Warning => ("", "SystemFillColorCautionBrush", "SystemFillColorCautionBackgroundBrush"),
            _ => ("", "AccentTextFillColorPrimaryBrush", "CardBackgroundFillColorDefaultBrush"),
        };
        MessageGlyph.Text = glyph;
        MessageGlyph.SetResourceReference(TextBlock.ForegroundProperty, brush);
        MessageBar.SetResourceReference(Border.BackgroundProperty, background);
        MessageText.Text = text;
        MessageBar.Visibility = Visibility.Visible;
    }

    private void HideMessage() => MessageBar.Visibility = Visibility.Collapsed;
}
