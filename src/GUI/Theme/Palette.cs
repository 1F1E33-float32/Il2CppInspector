using Avalonia;
using Avalonia.Media;

namespace Il2CppInspector.GUI.Theme;

internal static class Palette
{
    internal static readonly IBrush Text = Brush.Parse("#F0F5F5F7");
    internal static readonly IBrush Secondary = Brush.Parse("#A3EBEBEB");
    internal static readonly IBrush Tertiary = Brush.Parse("#6BDCDCDC");
    internal static readonly IBrush Separator = Brush.Parse("#1AFFFFFF");
    internal static readonly IBrush Fill = Brush.Parse("#12FFFFFF");
    internal static readonly IBrush Accent = Brush.Parse("#0A84FF");
    internal static readonly IBrush AccentText = Brush.Parse("#8DBDFF");
    internal static readonly IBrush AccentSoft = Brush.Parse("#290A84FF");
    internal static readonly IBrush OnAccentSecondary = Brush.Parse("#C7FFFFFF");
    internal static readonly IBrush OnAccentTrack = Brush.Parse("#40FFFFFF");
    internal static readonly IBrush ScrollThumb = Brush.Parse("#33FFFFFF");
    internal static readonly IBrush Danger = Brush.Parse("#FF453A");
    internal static readonly IBrush DangerText = Brush.Parse("#FF8A82");
    internal static readonly IBrush Success = Brush.Parse("#30D158");
    internal static readonly IBrush Warning = Brush.Parse("#FF9F0A");
    internal static readonly IBrush Table = Brush.Parse("#29000000");
    internal static readonly IBrush TableHeader = Brush.Parse("#1B1B1B");
    internal static readonly IBrush Stripe = Brush.Parse("#06FFFFFF");
    internal static readonly IBrush RowHover = Brush.Parse("#09FFFFFF");
    internal static readonly IBrush Background = Gradient("#1D1D1D", "#181818");
    internal static readonly IBrush StageBorder = Brush.Parse("#422F88EE");
    internal static readonly IBrush StageBackground = Brush.Parse("#172F88EE");
    internal static readonly IBrush StageFill = Gradient("#70B3FF", "#2F88EE", horizontal: true);
    internal static readonly IBrush AccentFill = Gradient("#409CFF", "#0A84FF", horizontal: true);

    private static LinearGradientBrush Gradient(string start, string end, bool horizontal = false) =>
        new()
        {
            StartPoint = new(0, 0, RelativeUnit.Relative),
            EndPoint = new(horizontal ? 1 : 0, horizontal ? 0 : 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Color.Parse(start), 0), new GradientStop(Color.Parse(end), 1)],
        };
}
