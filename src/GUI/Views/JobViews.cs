using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Il2CppInspector.GUI.Controls;
using Il2CppInspector.GUI.Models;
using Il2CppInspector.GUI.Theme;

namespace Il2CppInspector.GUI.Views;

internal sealed class TaskRow : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    private readonly AnalysisJob _job;
    private readonly TextBlock _title;
    private readonly TextBlock _meta = Ui.Text("", 11);
    private readonly Border _dot = new()
    {
        Width = 8,
        Height = 8,
        CornerRadius = new CornerRadius(4),
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly StageProgress _progress = new() { Margin = new Thickness(16, 0, 0, 0) };
    private bool _selected;

    internal TaskRow(AnalysisJob job, Action select)
    {
        _job = job;
        Height = double.NaN;
        BorderThickness = new Thickness(0);
        FontWeight = FontWeight.Normal;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Padding = new Thickness(10, 8);
        CornerRadius = new CornerRadius(7);
        Background = Brushes.Transparent;
        Grid heading = new() { ColumnDefinitions = new ColumnDefinitions("8,*") };
        heading.Children.Add(_dot);
        _title = Ui.Text(job.Title);
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _title.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_title, 1);
        heading.Children.Add(_title);
        _meta.TextTrimming = TextTrimming.CharacterEllipsis;
        _meta.Margin = new Thickness(16, 0, 0, 0);
        Content = new StackPanel { Spacing = 4, Children = { heading, _meta, _progress } };
        PointerEntered += (_, _) =>
        {
            if (!_selected)
                Background = Palette.RowHover;
        };
        PointerExited += (_, _) =>
        {
            if (!_selected)
                Background = Brushes.Transparent;
        };
        Click += (_, _) => select();
    }

    internal void Update(bool selected)
    {
        _selected = selected;
        Background =
            selected ? Palette.Accent
            : IsPointerOver ? Palette.RowHover
            : Brushes.Transparent;
        _title.Foreground = selected ? Brushes.White : Palette.Text;
        _meta.Foreground = selected ? Palette.OnAccentSecondary : Palette.Tertiary;
        _dot.Background = StatusBrush(_job.Status);
        JobStage stage = _job.ActiveStage;
        string state =
            _job.Cancelling && _job.Status == JobStatus.Running ? "Cancelling…"
            : _job.Status == JobStatus.Running ? stage?.Progress.Label ?? "Starting"
            : _job.Status.ToString();
        _meta.Text = $"{state} · {_job.Targets}";
        _progress.IsVisible = stage is not null;
        if (stage is not null)
            _progress.Update(stage.Ratio, selected ? Brushes.White : Palette.AccentFill, selected ? Palette.OnAccentTrack : Palette.Separator);
    }

    internal static IBrush StatusBrush(JobStatus status) =>
        status switch
        {
            JobStatus.Running => Palette.AccentText,
            JobStatus.Succeeded => Palette.Success,
            JobStatus.Cancelled => Palette.Warning,
            _ => Palette.Danger,
        };
}

internal sealed class StageBlock : Border
{
    private readonly TextBlock _title = Ui.Text("", 12.5);
    private readonly TextBlock _summary = Ui.Text("", 12.5, Palette.Secondary);
    private readonly StageProgress _progress = new();

    internal StageBlock()
    {
        Padding = new Thickness(12, 10, 12, 12);
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(1);
        BorderBrush = Palette.StageBorder;
        Background = Palette.StageBackground;
        Grid heading = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 5) };
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _summary.Margin = new Thickness(12, 0, 0, 0);
        heading.Children.Add(_title);
        Grid.SetColumn(_summary, 1);
        heading.Children.Add(_summary);
        Child = new StackPanel { Children = { heading, _progress } };
    }

    internal void Update(AnalysisJob job, JobStage stage)
    {
        _title.Inlines.Clear();
        _title.Inlines.Add(new Run(stage.Progress.Label));
        if (!string.IsNullOrEmpty(stage.Detail))
            _title.Inlines.Add(new Run("  " + stage.Detail) { Foreground = Palette.Tertiary });
        _summary.Text = stage.Summary;
        double? ratio = stage.Ratio;
        IBrush fill = Palette.StageFill;
        if (job.Status == JobStatus.Cancelled && (stage.Progress.Detail == "cancelled" || !stage.Ended))
        {
            ratio = 1;
            fill = Palette.Warning;
        }
        else if (stage.Failed)
        {
            ratio ??= 1;
            fill = Palette.Danger;
        }
        else if (!stage.Ended && job.Status != JobStatus.Running)
        {
            ratio = 1;
            fill = Palette.Danger;
        }
        _progress.Update(ratio, fill, Palette.Separator);
    }
}
