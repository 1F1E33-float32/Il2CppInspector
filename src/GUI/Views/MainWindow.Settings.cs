using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Il2CppInspector.GUI.Controls;
using Il2CppInspector.GUI.Theme;

namespace Il2CppInspector.GUI.Views;

internal sealed partial class MainWindow
{
    private Point? _settingsPosition;
    private Point? _dragOffset;
    private InputElement _previousFocus;

    private Border BuildSettings()
    {
        Grid header = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16, 0, 12, 0) };
        TextBlock title = Ui.Text("Settings", 12, Palette.Secondary);
        title.FontWeight = FontWeight.SemiBold;
        header.Children.Add(title);
        Button close = Ui.Button("Close", () => ToggleSettings(false));
        close.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        Border dragHeader = new()
        {
            Height = 42,
            Background = Brush.Parse("#08FFFFFF"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = Palette.Separator,
            Child = header,
        };
        dragHeader.PointerPressed += (_, e) =>
        {
            if (
                !e.GetCurrentPoint(dragHeader).Properties.IsLeftButtonPressed
                || e.Source is Visual source && (source is Button || source.GetVisualAncestors().Any(static ancestor => ancestor is Button))
            )
                return;
            _dragOffset = e.GetPosition(_root) - new Point(Canvas.GetLeft(_settings), Canvas.GetTop(_settings));
            e.Pointer.Capture(dragHeader);
            e.Handled = true;
        };
        dragHeader.PointerMoved += (_, e) =>
        {
            if (_dragOffset is not { } offset)
                return;
            Point pointer = e.GetPosition(_root);
            _settingsPosition = new Point(pointer.X - offset.X, pointer.Y - offset.Y);
            PositionSettings();
        };
        dragHeader.PointerReleased += (_, e) =>
        {
            _dragOffset = null;
            e.Pointer.Capture(null);
        };
        dragHeader.PointerCaptureLost += (_, _) => _dragOffset = null;
        StackPanel body = new()
        {
            Margin = new Thickness(18, 16, 18, 20),
            Spacing = 14,
            Children = { PathField("Startup metadata", _startup), Ui.Labeled("Unity version", _unityVersion), Ui.Labeled("Image base", _imageBase) },
        };
        return new Border
        {
            Width = 560,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush.Parse("#24FFFFFF"),
            Background = Brush.Parse("#F5222222"),
            ClipToBounds = true,
            BoxShadow = new BoxShadows(
                new BoxShadow
                {
                    OffsetY = 20,
                    Blur = 48,
                    Color = Color.FromArgb(153, 0, 0, 0),
                }
            ),
            Child = new StackPanel { Children = { dragHeader, body } },
        };
    }

    private void ToggleSettings(bool open)
    {
        if (open)
            _previousFocus = FocusManager?.GetFocusedElement() as InputElement;
        _settings.IsVisible = open;
        if (open)
            PositionSettings();
        else
            _previousFocus?.Focus();
    }

    private void PositionSettings()
    {
        if (!_settings.IsVisible)
            return;
        double width = Math.Min(560, Math.Max(0, _root.Bounds.Width - 32));
        _settings.Width = width;
        _settings.Measure(new Size(width, double.PositiveInfinity));
        Point position = _settingsPosition ?? new Point((_root.Bounds.Width - width) / 2, (_root.Bounds.Height - _settings.DesiredSize.Height) / 2);
        position = new Point(
            Math.Clamp(position.X, 0, Math.Max(0, _root.Bounds.Width - width)),
            Math.Clamp(position.Y, 0, Math.Max(0, _root.Bounds.Height - 42))
        );
        if (_settingsPosition is not null)
            _settingsPosition = position;
        Canvas.SetLeft(_settings, position.X);
        Canvas.SetTop(_settings, position.Y);
    }
}
