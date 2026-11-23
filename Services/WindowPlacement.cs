using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace FirewallDropper.Services;

/// <summary>
/// Keeps new windows and dialogs centered on the app window they came from, including windows opened by an
/// elevated copy of the app, which runs in a separate process. Those copies receive the caller's center point
/// in screen pixels as "--center x,y".
/// </summary>
public static class WindowPlacement
{
    public const string CenterFlag = "--center";

    /// <summary>Center point (screen pixels) passed in by the process that started this one, if any.</summary>
    public static Point? Hint { get; private set; }

    /// <summary>Removes a leading "--center x,y" from the arguments and remembers it.</summary>
    public static string[] ConsumeArgs(string[] args)
    {
        if (args.Length >= 2 && string.Equals(args[0], CenterFlag, StringComparison.OrdinalIgnoreCase))
        {
            var parts = args[1].Split(',');
            if (parts.Length == 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                Hint = new Point(x, y);
            return args[2..];
        }
        return args;
    }

    /// <summary>Arguments that tell an elevated copy where to center its windows.</summary>
    public static string[] ArgsFor(Window? from)
    {
        Point? center = ScreenCenterOf(from) ?? Hint;
        return center is Point p
            ? [CenterFlag, string.Create(CultureInfo.InvariantCulture, $"{p.X:0},{p.Y:0}")]
            : [];
    }

    /// <summary>The window the user is most likely looking at, used when no owner is given.</summary>
    public static Window? ActiveWindow() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
        ?? Application.Current?.Windows.OfType<Window>().LastOrDefault(w => w.IsVisible);

    /// <summary>
    /// Centers <paramref name="window"/> on <paramref name="owner"/> when there is one, otherwise on the hint passed
    /// from the starting process, otherwise on the screen. Call before Show/ShowDialog.
    /// </summary>
    public static void Place(Window window, Window? owner)
    {
        if (owner is { IsVisible: true } && !ReferenceEquals(owner, window))
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            return;
        }

        if (Hint is not Point hint)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        bool placed = false;
        // SizeChanged fires once the window has measured itself, before it's first drawn, so there's no visible jump.
        window.SizeChanged += (_, _) =>
        {
            if (placed) return;
            placed = true;
            var source = PresentationSource.FromVisual(window);
            Matrix toDip = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            Point c = toDip.Transform(hint);
            window.Left = c.X - window.ActualWidth / 2;
            window.Top = Math.Max(0, c.Y - window.ActualHeight / 2);
        };
    }

    private static Point? ScreenCenterOf(Window? w)
    {
        if (w is not { IsVisible: true } || PresentationSource.FromVisual(w)?.CompositionTarget is not { } target)
            return null;
        // Use the outer frame (Left/Top/ActualWidth/Height are in device-independent units), then convert to pixels.
        var center = new Point(w.Left + w.ActualWidth / 2, w.Top + w.ActualHeight / 2);
        if (w.WindowState == WindowState.Maximized)
        {
            var area = SystemParameters.WorkArea;
            center = new Point(area.Left + area.Width / 2, area.Top + area.Height / 2);
        }
        return target.TransformToDevice.Transform(center);
    }
}
