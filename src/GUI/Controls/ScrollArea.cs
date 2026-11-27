using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Il2CppInspector.GUI.Theme;

namespace Il2CppInspector.GUI.Controls;

internal sealed class ScrollArea : Grid
{
    private readonly Border _thumb = new()
    {
        Width = 6,
        IsVisible = false,
        CornerRadius = new CornerRadius(3),
        Background = Palette.ScrollThumb,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        IsHitTestVisible = false,
    };

    internal ScrollViewer Viewer { get; }

    internal ScrollArea(Control content)
    {
        Viewer = new()
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        Children.Add(Viewer);
        Children.Add(_thumb);
        Viewer.ScrollChanged += (_, _) => UpdateThumb();
        SizeChanged += (_, _) => UpdateThumb();
    }

    private void UpdateThumb()
    {
        double view = Viewer.Viewport.Height;
        double hidden = Viewer.Extent.Height - view;
        _thumb.IsVisible = hidden > 0 && view > 0;
        if (!_thumb.IsVisible)
            return;
        double height = Math.Min(view, Math.Max(24, view * view / (view + hidden)));
        _thumb.Height = height;
        _thumb.Margin = new Thickness(0, (view - height) * Math.Clamp(Viewer.Offset.Y / hidden, 0, 1), 2, 0);
    }
}
