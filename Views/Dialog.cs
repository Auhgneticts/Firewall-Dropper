using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FirewallDropper.Services;

namespace FirewallDropper.Views;

/// <summary>
/// The app's message box: matches the Fluent theme and always centers on the window it was opened from
/// (the standard Windows message box doesn't reliably do that).
/// </summary>
public sealed class Dialog : Window
{
    private MessageBoxResult _result;

    public static MessageBoxResult Show(Window? owner, string message,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        var dialog = new Dialog(message, buttons, image);
        WindowPlacement.Place(dialog, owner ?? WindowPlacement.ActiveWindow());
        dialog.ShowDialog();
        return dialog._result;
    }

    private Dialog(string message, MessageBoxButton buttons, MessageBoxImage image)
    {
        Title = AppInfo.Name;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        MinWidth = 380;
        MaxWidth = 560;
        if (Application.Current?.TryFindResource("AppIcon") is ImageSource appIcon) Icon = appIcon;

        // Closing with the X (or Esc) counts as the "no / cancel" answer.
        _result = buttons switch
        {
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.OK => MessageBoxResult.OK,
            _ => MessageBoxResult.Cancel,
        };

        var body = new Grid { Margin = new Thickness(24, 20, 24, 20) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.RowDefinitions.Add(new RowDefinition());
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var (glyph, brushKey) = image switch
        {
            MessageBoxImage.Error => ("", "SystemFillColorCriticalBrush"),
            MessageBoxImage.Warning => ("", "SystemFillColorCautionBrush"),
            MessageBoxImage.Question => ("", "AccentTextFillColorPrimaryBrush"),
            MessageBoxImage.Information => ("", "AccentTextFillColorPrimaryBrush"),
            _ => ((string?)null, (string?)null),
        };
        if (glyph is not null)
        {
            var icon = new TextBlock
            {
                Text = glyph,
                FontSize = 24,
                Margin = new Thickness(0, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };
            if (Application.Current?.TryFindResource("Glyph") is Style glyphStyle) icon.Style = glyphStyle;
            icon.FontSize = 24;
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.SetResourceReference(TextBlock.ForegroundProperty, brushKey!);
            body.Children.Add(icon);
        }

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 460,
        };
        Grid.SetColumn(text, 1);
        body.Children.Add(text);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 24, 0, 0),
        };
        Grid.SetRow(buttonRow, 1);
        Grid.SetColumnSpan(buttonRow, 2);
        body.Children.Add(buttonRow);

        var spec = buttons switch
        {
            MessageBoxButton.OKCancel => new[] { ("OK", MessageBoxResult.OK), ("Cancel", MessageBoxResult.Cancel) },
            MessageBoxButton.YesNo => [("Yes", MessageBoxResult.Yes), ("No", MessageBoxResult.No)],
            MessageBoxButton.YesNoCancel => [("Yes", MessageBoxResult.Yes), ("No", MessageBoxResult.No), ("Cancel", MessageBoxResult.Cancel)],
            _ => [("OK", MessageBoxResult.OK)],
        };

        for (int i = 0; i < spec.Length; i++)
        {
            var (label, result) = spec[i];
            var button = new Button
            {
                Content = label,
                MinWidth = 96,
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                IsDefault = i == 0,
                IsCancel = i == spec.Length - 1,
            };
            if (i == 0) button.SetResourceReference(StyleProperty, "AccentButtonStyle");
            button.Click += (_, _) =>
            {
                _result = result;
                DialogResult = result is MessageBoxResult.OK or MessageBoxResult.Yes;
            };
            buttonRow.Children.Add(button);
        }

        Content = body;
        Loaded += (_, _) => (buttonRow.Children[0] as Button)?.Focus();
    }
}
