using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Il2CppInspector.CLI
{
    internal static class ProgressBar
    {
        private static readonly object Sync = new();
        private static readonly Stopwatch Elapsed = new();
        private static OperationProgress? active;
        private static OperationProgress? lastWritten;
        private static int lastLineLength;
        private static int lastBucket = -2;
        private static long lastRefresh;

        // Set by --json-progress: progress and status messages become one JSON object per line for front ends.
        public static bool JsonLines { get; set; }

        internal static IDisposable RefreshContinuously() =>
            new Timer(
                static _ =>
                {
                    lock (Sync)
                        Draw(false);
                },
                null,
                100,
                100
            );

        public static void Update(OperationProgress progress)
        {
            lock (Sync)
            {
                if (active is null || active.Value.Label != progress.Label)
                {
                    FinishLine();
                    RestartTiming();
                }
                active = progress;
                var complete = progress.Total >= 0 && progress.Current >= progress.Total;
                var ended = progress.Detail is "done" or "failed" or "cancelled";
                Draw(complete || ended);
                if (complete || ended)
                    FinishLine();
            }
        }

        public static void WriteStatus(string message)
        {
            lock (Sync)
            {
                FinishLine();
                Console.WriteLine(JsonLines ? JsonSerializer.Serialize(new { @event = "status", message }) : $"  {message}");
            }
        }

        internal static string Render(OperationProgress progress, TimeSpan elapsed, int width = int.MaxValue)
        {
            int percent = Percentage(progress);
            int barLength = Math.Clamp(width - 30, 5, 30);
            int filled = percent < 0 ? 0 : percent * barLength / 100;
            char[] bar = Enumerable.Repeat('-', barLength).ToArray();
            if (percent < 0)
            {
                int position = (int)(elapsed.TotalMilliseconds / 100 % (barLength - 2));
                Array.Fill(bar, '#', position, 3);
            }
            else
                Array.Fill(bar, '#', 0, filled);
            string suffix =
                $" [{new string(bar)}] {(percent < 0 ? " --" : percent.ToString(CultureInfo.InvariantCulture).PadLeft(3))}% ({elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}s)";
            string label = Fit(progress.Label, Math.Max(0, width - suffix.Length - 2));
            string line = "  " + label + suffix;
            if (!string.IsNullOrEmpty(progress.Detail))
                line += Fit(" " + progress.Detail, Math.Max(0, width - line.Length));
            return Fit(line, width);
        }

        private static int Percentage(OperationProgress progress) =>
            progress.Total < 0 ? -1
            : progress.Total == 0 ? 100
            : (int)(Math.Clamp((double)progress.Current / progress.Total, 0, 1) * 100);

        private static string Fit(string value, int width) =>
            value.Length <= width ? value
            : width <= 3 ? value[..width]
            : value[..(width - 3)] + "...";

        private static void Draw(bool force)
        {
            if (active is null)
                return;
            if (JsonLines)
            {
                // The refresh timer redraws unchanged progress; a parser only needs changes, throttled like the interactive bar.
                if (!force && (active == lastWritten || Elapsed.ElapsedMilliseconds - lastRefresh < 100))
                    return;
                var (label, current, total, detail) = active.Value;
                Console.WriteLine(
                    JsonSerializer.Serialize(
                        new
                        {
                            @event = "progress",
                            label,
                            current,
                            total,
                            detail,
                        }
                    )
                );
                lastWritten = active;
                lastRefresh = Elapsed.ElapsedMilliseconds;
                return;
            }
            int bucket = Percentage(active.Value) / 10;
            if (Console.IsOutputRedirected)
            {
                if (!force && bucket == lastBucket)
                    return;
                Console.WriteLine(Render(active.Value, Elapsed.Elapsed));
            }
            else
            {
                if (!force && Elapsed.ElapsedMilliseconds - lastRefresh < 100)
                    return;
                int width = Console.WindowWidth > 1 ? Console.WindowWidth - 1 : 80;
                string line = Render(active.Value, Elapsed.Elapsed, width);
                Console.Write('\r' + line + new string(' ', Math.Max(0, Math.Min(lastLineLength, width) - line.Length)));
                lastLineLength = line.Length;
            }
            lastBucket = bucket;
            lastRefresh = Elapsed.ElapsedMilliseconds;
        }

        private static void RestartTiming()
        {
            Elapsed.Restart();
            lastWritten = null;
            lastBucket = -2;
            lastRefresh = -100;
        }

        private static void FinishLine()
        {
            if (active != null && !JsonLines && !Console.IsOutputRedirected)
                Console.WriteLine();
            active = null;
            lastLineLength = 0;
        }
    }
}
