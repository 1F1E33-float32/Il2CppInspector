using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Il2CppInspector.GUI.Theme;
using Il2CppInspector.Plugins;

namespace Il2CppInspector.GUI.Controls;

internal sealed class GamePicker : Border
{
    private static readonly string[] Titles = ["Game", "Region", "Platform", "Version"];
    private static readonly double[] Widths = [172, 112, 124, 197];
    private readonly StackPanel _columns = new() { Orientation = Orientation.Horizontal };
    private readonly List<string> _path = [];
    private readonly List<Column> _views = [];
    private readonly FrameLoop _animation;
    private GamePlugin[] _games = [];
    private readonly TextBlock _note = Ui.Message("Loading game plugins…");
    private long _opened;
    private int _openedFrom;

    internal event Action SelectionChanged;
    internal double PreferredHeight { get; private set; }
    internal GamePluginInputs? Inputs => _path.Count == 4 ? _games.First(g => g.GameId == string.Join('_', _path)).RequiredInputs : null;

    internal GamePicker()
    {
        MinHeight = 150;
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        BorderBrush = Palette.Separator;
        Background = Palette.Table;
        ClipToBounds = true;
        Grid content = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        content.Children.Add(_columns);
        Grid filler = new() { RowDefinitions = new RowDefinitions("26,*") };
        filler.Children.Add(Header(""));
        Grid.SetRow(_note, 1);
        filler.Children.Add(_note);
        Grid.SetColumn(filler, 1);
        content.Children.Add(filler);
        Child = content;
        _animation = new(this, Animate);
        Rebuild(0);
    }

    internal void Load(GamePlugin[] games)
    {
        _games = games;
        _note.IsVisible = false;
        Rebuild(0);
        SelectionChanged?.Invoke();
    }

    internal void ShowError(string message)
    {
        _note.Text = message;
        _note.Foreground = Palette.DangerText;
        _note.IsVisible = true;
    }

    internal string GetGame()
    {
        if (_path.Count is > 0 and < 4)
            throw new ArgumentException($"Choose a {Titles[_path.Count].ToLowerInvariant()} for {string.Join(' ', _path)}.");
        return _path.Count == 0 ? null : string.Join('_', _path);
    }

    private string[] Options(int depth) =>
        _games
            .Select(static g => g.GameId.Split('_'))
            .Where(parts => parts.Length == 4 && parts.All(static p => p.Length != 0) && parts.Take(depth).SequenceEqual(_path.Take(depth)))
            .Select(parts => parts[depth])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private void Choose(int depth, string value)
    {
        if (value is null ? _path.Count == 0 : _path.Count > depth && _path[depth] == value)
            return;
        _path.RemoveRange(depth, _path.Count - depth);
        if (value is not null)
            _path.Add(value);
        while (_path.Count is > 0 and < 4 && Options(_path.Count) is [var only])
            _path.Add(only);
        Rebuild(depth + 1);
        SelectionChanged?.Invoke();
    }

    private void Rebuild(int from)
    {
        int count = Math.Min(_path.Count + 1, 4);
        while (_views.Count > from)
        {
            _columns.Children.Remove(_views[^1].Clip);
            _views.RemoveAt(_views.Count - 1);
        }
        int maxRows = 0;
        for (int depth = 0; depth < count; depth++)
        {
            string[] values = depth == 0 ? [null, .. Options(depth)] : Options(depth);
            maxRows = Math.Max(maxRows, values.Length);
            Column column;
            if (depth < _views.Count)
                column = _views[depth];
            else
            {
                StackPanel rows = new();
                Grid body = new() { RowDefinitions = new RowDefinitions("26,*") };
                body.Children.Add(Header(Titles[depth]));
                ScrollArea scroll = new(rows);
                Grid.SetRow(scroll, 1);
                body.Children.Add(scroll);
                Border surface = new()
                {
                    Width = Widths[depth],
                    Child = body,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    BorderThickness = new Thickness(0, 0, depth < 3 ? 1 : 0, 0),
                    BorderBrush = Palette.Separator,
                };
                Border clip = new()
                {
                    Child = surface,
                    Width = depth == 0 ? Widths[depth] : 0,
                    ClipToBounds = true,
                };
                column = new(clip, rows);
                _views.Add(column);
                _columns.Children.Add(clip);
            }
            column.Rows.Children.Clear();
            for (int index = 0; index < values.Length; index++)
                column.Rows.Children.Add(Row(depth, index, values[index]));
        }
        PreferredHeight = Math.Max(150, maxRows * 30 + 28);
        _openedFrom = Math.Max(1, from);
        _opened = Stopwatch.GetTimestamp();
        if (_views.Count > _openedFrom)
            _animation.Start();
        InvalidateMeasure();
    }

    private Control Row(int depth, int index, string value)
    {
        var selected = value is null ? _path.Count == 0 : _path.Count > depth && _path[depth] == value;
        var current = selected && depth == Math.Max(0, _path.Count - 1);
        var background =
            current ? Palette.Accent
            : selected ? Palette.AccentSoft
            : index % 2 == 1 ? Palette.Stripe
            : Brushes.Transparent;
        Grid content = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(10, 6) };
        var title = Ui.Text(value ?? "Auto-detect", color: current ? Brushes.White : Palette.Text);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        content.Children.Add(title);
        if (value is not null && depth < 3)
        {
            TextBlock arrow = Ui.Text("›", color: current ? Palette.OnAccentSecondary : Palette.Tertiary);
            arrow.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(arrow, 1);
            content.Children.Add(arrow);
        }
        Button row = new()
        {
            FontWeight = FontWeight.Normal,
            Content = content,
            Height = 30,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Background = background,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        row.PointerEntered += (_, _) =>
        {
            if (!selected)
                row.Background = Palette.Fill;
        };
        row.PointerExited += (_, _) => row.Background = background;
        row.Click += (_, _) => Choose(depth, value);
        return row;
    }

    private bool Animate()
    {
        double elapsed = Stopwatch.GetElapsedTime(_opened).TotalMilliseconds;
        bool running = false;
        for (int depth = _openedFrom; depth < _views.Count; depth++)
        {
            double t = Math.Clamp((elapsed - (depth - _openedFrom) * 35) / 140, 0, 1);
            double open = 1 - Math.Pow(1 - t, 5);
            _views[depth].Clip.Width = Widths[depth] * open;
            _views[depth].Clip.Opacity = open;
            running |= t < 1;
        }
        return running;
    }

    private static Border Header(string title) =>
        new()
        {
            Height = 26,
            Background = Palette.TableHeader,
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = Palette.Separator,
            Padding = new Thickness(10, 0),
            Child = new TextBlock
            {
                Text = title,
                FontSize = 11,
                FontWeight = FontWeight.Medium,
                Foreground = Palette.Tertiary,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

    private sealed record Column(Border Clip, StackPanel Rows);
}
