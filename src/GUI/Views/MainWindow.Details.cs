using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Il2CppInspector.GUI.Controls;
using Il2CppInspector.GUI.Models;
using Il2CppInspector.GUI.Theme;

namespace Il2CppInspector.GUI.Views;

internal sealed partial class MainWindow
{
    private ScrollArea _detailsScroll;
    private StackPanel _stages;
    private TextBlock _status;
    private SelectableTextBlock _jobError;
    private Button _remove;
    private Button _openOutput;

    private void RebuildDetails()
    {
        if (_selected is null)
        {
            _details.Child = Ui.Message("Select a task to see its progress");
            return;
        }
        AnalysisJob job = _selected;
        Grid column = new() { RowDefinitions = new RowDefinitions("52,*") };
        Grid header = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16, 0, 12, 0) };
        TextBlock title = Ui.Text($"{job.Title} · {job.Targets}", 12, Palette.Secondary);
        title.FontWeight = FontWeight.SemiBold;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Margin = new Thickness(0, 0, 8, 0);
        header.Children.Add(title);
        _openOutput = Ui.Button("Open output", () => OpenOutput(job));
        _remove = Ui.Button("Cancel", () => Remove(job));
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _openOutput, Ui.Button("Copy", () => _ = CopyLog(job)), _remove },
        };
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        column.Children.Add(header);
        StackPanel paths = new() { Spacing = 4 };
        foreach ((string label, string value) in job.Details())
        {
            Grid row = new() { ColumnDefinitions = new ColumnDefinitions("110,*") };
            TextBlock name = Ui.Text(label, 12, Palette.Tertiary);
            name.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(name);
            SelectableTextBlock text = new()
            {
                Text = value,
                FontSize = 12,
                LineHeight = 18,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Palette.Text,
                Margin = new Thickness(12, 0, 0, 0),
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            paths.Children.Add(row);
        }
        _stages = new() { Spacing = 12 };
        _status = Ui.Text("");
        _status.FontWeight = FontWeight.SemiBold;
        _jobError = new()
        {
            Foreground = Palette.DangerText,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18,
        };
        StackPanel body = new()
        {
            Margin = new Thickness(16, 0, 16, 24),
            Spacing = 12,
            Children = { paths, _stages, _status, _jobError },
        };
        _detailsScroll = new(body);
        Grid.SetRow(_detailsScroll, 1);
        column.Children.Add(_detailsScroll);
        _details.Child = column;
        RefreshDetails(true);
        FollowDetails();
    }

    private void RefreshDetails(bool changed)
    {
        var job = _selected;
        if (job is null)
            return;
        var viewer = _detailsScroll.Viewer;
        var following = viewer.Extent.Height - viewer.Viewport.Height - viewer.Offset.Y < 32;
        while (_stages.Children.Count < job.Stages.Count)
            _stages.Children.Add(new StageBlock());
        for (var index = 0; index < job.Stages.Count; index++)
            ((StageBlock)_stages.Children[index]).Update(job, job.Stages[index]);
        var running = job.Status == JobStatus.Running;
        _status.IsVisible = !running;
        _status.Text = job.Status.ToString();
        _status.Foreground = job.Status == JobStatus.Failed ? Palette.DangerText : TaskRow.StatusBrush(job.Status);
        _jobError.Text = job.Error;
        _jobError.IsVisible = job.Error is not null;
        _remove.Content = running
            ? job.Cancelling
                ? "Cancelling…"
                : "Cancel"
            : "Remove";
        _remove.Classes.Set("danger", running);
        _remove.IsEnabled = !running || !job.Cancelling;
        _openOutput.IsEnabled = Directory.Exists(job.Request.OutputDir);
        if (following && changed)
            FollowDetails();
    }

    private void FollowDetails()
    {
        ScrollArea scroll = _detailsScroll;
        // Layout must include newly appended stages before the final scroll offset is calculated.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (scroll == _detailsScroll)
                    scroll.Viewer.ScrollToEnd();
            },
            DispatcherPriority.Loaded
        );
    }

    private void Remove(AnalysisJob job)
    {
        if (job.Status == JobStatus.Running)
        {
            job.Cancel();
            RefreshDetails(true);
            return;
        }
        int index = _jobs.FindIndex(pair => pair.Job == job);
        _taskList.Children.Remove(_jobs[index].Row);
        _jobs.RemoveAt(index);
        _noTasks.IsVisible = _jobs.Count == 0;
        if (_selected == job)
        {
            _selected = null;
            RebuildDetails();
        }
    }

    private async Task CopyLog(AnalysisJob job)
    {
        try
        {
            if (Clipboard is not null)
                await Clipboard.SetTextAsync(job.CopyLog());
        }
        catch (Exception ex)
        {
            ShowError($"Cannot copy the task log: {ex.Message}");
        }
    }

    private void OpenOutput(AnalysisJob job)
    {
        try
        {
            using Process process = Process.Start(new ProcessStartInfo(job.Request.OutputDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError($"Cannot open output directory: {ex.Message}");
        }
    }
}
