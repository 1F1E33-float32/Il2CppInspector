using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Il2CppInspector.GUI.Theme;

namespace Il2CppInspector.GUI.Controls;

internal sealed class StageProgress : Control
{
    private readonly FrameLoop _animation;
    private readonly long _started = Stopwatch.GetTimestamp();
    private double? _ratio;
    private IBrush _fill = Palette.StageFill;
    private IBrush _track = Palette.Separator;

    internal StageProgress()
    {
        Height = 6;
        ClipToBounds = true;
        _animation = new(
            this,
            () =>
            {
                InvalidateVisual();
                return _ratio is null && IsVisible;
            }
        );
        AttachedToVisualTree += (_, _) => Update(_ratio, _fill, _track);
    }

    internal void Update(double? ratio, IBrush fill, IBrush track)
    {
        _ratio = ratio;
        _fill = fill;
        _track = track;
        InvalidateVisual();
        if (ratio is null)
            _animation.Start();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.DrawRectangle(_track, null, bounds, 3, 3);
        using var clip = context.PushClip(new RoundedRect(bounds, 3));
        if (_ratio is { } ratio)
            context.DrawRectangle(_fill, null, new Rect(0, 0, bounds.Width * Math.Clamp(ratio, 0, 1), 6), 3, 3);
        else
        {
            double t = Stopwatch.GetElapsedTime(_started).TotalMilliseconds % 1200 / 1200;
            double eased = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
            context.DrawRectangle(_fill, null, new Rect(bounds.Width * (-0.35 + 1.35 * eased), 0, bounds.Width * 0.35, 6), 3, 3);
        }
    }
}
