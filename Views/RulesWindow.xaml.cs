using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using FirewallDropper.Services;

namespace FirewallDropper.Views;

public sealed class RuleRow(ManagedRule rule)
{
    public ManagedRule Rule { get; } = rule;
    public ImageSource? Icon { get; } = IconHelper.Get(rule.ProgramPath);
    public Visibility MissingVisibility => Rule.ProgramExists ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>Lists the rules Firewall Dropper created and lets the user turn them on/off or remove them.</summary>
public partial class RulesWindow : Window
{
    private static RulesWindow? _open;
    private List<RuleRow> _rows = [];
    private bool _busy;

    public static void ShowSingle(Window? owner)
    {
        if (_open is not null)
        {
            if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
            _open.Activate();
            return;
        }
        _open = new RulesWindow();
        if (owner is { IsVisible: true }) _open.Owner = owner;
        else _open.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _open.Closed += (_, _) => _open = null;
        _open.Show();
    }

    public RulesWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        StatusText.Text = "Loading…";
        try
        {
            var rules = await Task.Run(FirewallService.GetManagedRules);
            _rows = rules.OrderBy(r => r.ProgramName, StringComparer.CurrentCultureIgnoreCase)
                         .ThenBy(r => r.Direction)
                         .Select(r => new RuleRow(r))
                         .ToList();
            RulesGrid.ItemsSource = _rows;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            _rows = [];
            RulesGrid.ItemsSource = _rows;
            StatusText.Text = "Couldn't read Windows Firewall: " + FirewallService.Describe(ex);
        }
        UpdateState();
    }

    private void ApplyFilter()
    {
        var text = SearchBox.Text.Trim();
        SearchHint.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var view = CollectionViewSource.GetDefaultView(RulesGrid.ItemsSource);
        if (view is null) return;
        view.Filter = text.Length == 0
            ? null
            : o => o is RuleRow r &&
                   (r.Rule.ProgramPath.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    r.Rule.Name.Contains(text, StringComparison.OrdinalIgnoreCase));
        UpdateState();
    }

    private void UpdateState()
    {
        var selected = Selected();
        EnableButton.IsEnabled = !_busy && selected.Any(r => !r.Rule.Enabled);
        DisableButton.IsEnabled = !_busy && selected.Any(r => r.Rule.Enabled);
        RemoveButton.IsEnabled = !_busy && selected.Count > 0;
        RemoveAllButton.IsEnabled = !_busy && _rows.Count > 0;
        EmptyState.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_busy) return;
        int programs = _rows.Select(r => r.Rule.ProgramPath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        StatusText.Text = _rows.Count == 0 ? "" :
            $"{_rows.Count} rule{(_rows.Count == 1 ? "" : "s")} for {programs} program{(programs == 1 ? "" : "s")}" +
            (selected.Count > 0 ? $" · {selected.Count} selected" : "") +
            (AppInfo.IsAdministrator ? "" : " · Changes will ask for administrator permission");
    }

    private List<RuleRow> Selected() => RulesGrid.SelectedItems.OfType<RuleRow>().ToList();

    private async Task RunAsync(string op, IReadOnlyList<string> names, string working)
    {
        _busy = true;
        UpdateState();
        StatusText.Text = working;
        bool? ok = await RuleOperations.RunAsync(op, names);
        _busy = false;
        await LoadAsync();
        if (ok is null)
            StatusText.Text = "Administrator permission was declined. No changes were made.";
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void Enable_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(RuleOperations.Enable, Selected().Where(r => !r.Rule.Enabled).Select(r => r.Rule.Name).ToList(), "Turning on…");

    private async void Disable_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(RuleOperations.Disable, Selected().Where(r => r.Rule.Enabled).Select(r => r.Rule.Name).ToList(), "Turning off…");

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count == 0) return;
        string what = selected.Count == 1
            ? $"the {selected[0].Rule.DirectionText.ToLowerInvariant()} rule for {selected[0].Rule.ProgramName}"
            : $"{selected.Count} rules";
        if (MessageBox.Show(this, $"Remove {what} from Windows Firewall?", AppInfo.Name,
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        await RunAsync(RuleOperations.Remove, selected.Select(r => r.Rule.Name).ToList(), "Removing…");
    }

    private async void RemoveAll_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"Remove all {_rows.Count} rules created by Firewall Dropper from Windows Firewall?",
                AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        await RunAsync(RuleOperations.RemoveAll, [], "Removing all…");
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateState();

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Double-click shows the program in File Explorer.
        if (RulesGrid.SelectedItem is RuleRow { Rule.ProgramExists: true } row)
            Process.Start("explorer.exe", $"/select,\"{row.Rule.ProgramPath}\"");
    }

    private void OpenWindowsFirewall_Click(object sender, RoutedEventArgs e) => MainWindow.OpenWindowsFirewall(this);
}
