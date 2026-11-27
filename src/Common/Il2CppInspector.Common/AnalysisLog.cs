namespace Il2CppInspector;

// The execution context carries diagnostics through existing readers without redirecting process-wide Console streams.
// AsyncLocal also keeps parallel GUI jobs and nested silent operations independent.
internal sealed class AnalysisLog : IDisposable
{
    private static readonly AsyncLocal<Action<string>> Sink = new();
    private static readonly AsyncLocal<CancellationToken> Cancellation = new();
    private readonly Action<string> _previous;
    private readonly CancellationToken _previousCancellation;

    internal AnalysisLog(Action<string> sink, CancellationToken? cancellationToken = null)
    {
        _previous = Sink.Value;
        _previousCancellation = Cancellation.Value;
        Sink.Value = sink;
        if (cancellationToken.HasValue)
            Cancellation.Value = cancellationToken.Value;
    }

    internal static void CheckCancellation() => Cancellation.Value.ThrowIfCancellationRequested();

    internal static void WriteLine(string message = "")
    {
        CheckCancellation();
        Sink.Value?.Invoke(message);
    }

    internal static void WriteLine(string format, params object[] arguments) => WriteLine(string.Format(format, arguments));

    public void Dispose()
    {
        Sink.Value = _previous;
        Cancellation.Value = _previousCancellation;
    }
}
