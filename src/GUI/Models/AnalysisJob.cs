using System.Collections.Concurrent;
using System.Diagnostics;
using Il2CppInspector.Analysis;

namespace Il2CppInspector.GUI.Models;

internal enum JobStatus
{
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

internal sealed class JobStage(OperationProgress progress)
{
    private readonly long _started = Stopwatch.GetTimestamp();
    private long? _finished;

    internal OperationProgress Progress { get; set; } = progress;
    internal bool Failed => Progress.Detail == "failed";
    internal bool Ended => Progress.Detail is "done" or "failed" or "cancelled";
    internal double? Ratio =>
        Progress.Total < 0 ? null
        : Progress.Total == 0 ? 1
        : Math.Clamp((double)Progress.Current / Progress.Total, 0, 1);
    internal string Detail => Progress.Detail is "done" or "failed" or "cancelled" ? null : Progress.Detail;
    internal string Summary
    {
        get
        {
            double seconds = Stopwatch.GetElapsedTime(_started, _finished ?? Stopwatch.GetTimestamp()).TotalSeconds;
            string elapsed = $"{seconds:F1} s";
            return Ratio is { } ratio ? $"{Math.Floor(ratio * 100)}% · {elapsed}" : elapsed;
        }
    }

    internal void Finish() => _finished ??= Stopwatch.GetTimestamp();
}

internal sealed class AnalysisJob
{
    private readonly ConcurrentQueue<Update> _updates = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly List<string> _lines = [];

    internal AnalysisRequest Request { get; }
    internal JobStatus Status { get; private set; }
    internal List<JobStage> Stages { get; } = [];
    internal string Error { get; private set; }
    internal bool Cancelling { get; private set; }
    internal Task Completion { get; }
    internal string Title => Request.Game ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(Request.OutputDir));
    internal string Targets => Request.Targets.Count == 0 ? "DummyDll" : string.Join(" / ", Request.Targets);
    internal JobStage ActiveStage => Status == JobStatus.Running && Stages.LastOrDefault() is { Ended: false } stage ? stage : null;

    internal AnalysisJob(AnalysisRequest request)
    {
        Request = request;
        Completion = Task.Run(() =>
        {
            try
            {
                new AnalysisRunner(
                    progress => _updates.Enqueue(new(progress, null, null)),
                    line => _updates.Enqueue(new(null, line, null)),
                    _cancellation.Token
                ).Run(request);
                _updates.Enqueue(new(null, null, JobStatus.Succeeded));
            }
            catch (OperationCanceledException)
            {
                _updates.Enqueue(new(null, null, JobStatus.Cancelled));
            }
            catch (Exception ex)
            {
                if (ex is AggregateException aggregate && aggregate.Flatten().InnerExceptions.All(static e => e is OperationCanceledException))
                    _updates.Enqueue(new(null, null, JobStatus.Cancelled));
                else
                    _updates.Enqueue(new(null, ex.Message, JobStatus.Failed));
            }
        });
    }

    internal bool Drain()
    {
        var changed = false;
        while (_updates.TryDequeue(out var update))
        {
            changed = true;
            if (update.Progress is { } progress)
            {
                var last = Stages.LastOrDefault();
                if (last is null || last.Progress.Label != progress.Label || last.Ended)
                {
                    last?.Finish();
                    last = new(progress);
                    Stages.Add(last);
                }
                else
                    last.Progress = progress;
                if (last.Ended || progress.Total >= 0 && progress.Current >= progress.Total)
                    last.Finish();
            }
            else if (update.Status is { } status)
            {
                Status = status;
                Error = update.Line;
                Stages.LastOrDefault()?.Finish();
                _cancellation.Dispose();
            }
            else
                _lines.Add(update.Line);
        }
        return changed;
    }

    internal void Cancel()
    {
        if (Status != JobStatus.Running || Cancelling)
            return;
        Cancelling = true;
        _cancellation.Cancel();
    }

    internal IEnumerable<(string Label, string Value)> Details()
    {
        if (Request.BinaryFile is not null)
            yield return ("Binary", Request.BinaryFile);
        if (Request.MetadataFile is not null)
            yield return ("Metadata", Request.MetadataFile);
        yield return ("Output", Request.OutputDir);
        yield return ("Game", Request.Game ?? "Auto-detect");
        yield return ("Targets", Targets);
        if (Request.StartupMetadataFile is not null)
            yield return ("Startup metadata", Request.StartupMetadataFile);
        if (Request.UnityVersion is not null)
            yield return ("Unity version", Request.UnityVersion);
        if (Request.ImageBase is not null)
            yield return ("Image base", Request.ImageBase);
    }

    internal string CopyLog()
    {
        var header = Details().Select(static pair => $"{pair.Label}: {pair.Value}");
        var stages = Stages.Select(static stage => $"{stage.Progress.Label} [{stage.Summary}]");
        string[] error = Error is null ? [] : [Error];
        return string.Join('\n', header.Concat(stages).Concat(_lines).Concat(error).Append(Status.ToString()));
    }

    // Directory boundaries prevent "out/game" from colliding with "out/game2"; include the sibling output-N image trees.
    internal static bool OutputsOverlap(string first, string second)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        first = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first));
        second = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second));
        return Contains(first, second, comparison) || Contains(second, first, comparison);

        static bool Contains(string parent, string child, StringComparison comparison)
        {
            if (
                parent.Equals(child, comparison)
                || child.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, comparison)
            )
                return true;
            if (!child.StartsWith(parent + "-", comparison))
                return false;
            ReadOnlySpan<char> suffix = child.AsSpan(parent.Length + 1);
            int end = suffix.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return int.TryParse(end < 0 ? suffix : suffix[..end], out int imageIndex) && imageIndex >= 0;
        }
    }

    private readonly record struct Update(OperationProgress? Progress, string Line, JobStatus? Status);
}
