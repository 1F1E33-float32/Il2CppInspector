namespace Il2CppInspector;

// Reports one operation made of phases that count different kinds of work. Each phase is planned with a weight, its
// estimated share of the time, before the operation starts, and later moves through that share by its own step count.
// A phase always ends at its planned boundary, so the progress stays monotonic and reaches the end even when an
// estimate or step count is off. Progress is reported in thousandths because the phases have no common unit.
internal sealed class PhasedProgress(string label, Action<OperationProgress> callback)
{
    private const long Scale = 1000;

    private readonly Dictionary<string, double> _weights = [];
    private double _total;
    private double _start;
    private double _weight;
    private long _steps;
    private long _done;
    private long _reported = -1;
    private string _detail;

    public void Plan(string phase, double weight)
    {
        if (weight <= 0)
            return;
        _weights[phase] = _weights.GetValueOrDefault(phase) + weight;
        _total += weight;
    }

    // Starts a planned phase; zero steps means it only reports its start and end. Unplanned phases take no share.
    public void Begin(string phase, long steps = 0)
    {
        End();
        _detail = phase;
        _weight = _weights.GetValueOrDefault(phase);
        _steps = steps;
        _done = 0;
        Report(true);
    }

    public void Step()
    {
        AnalysisLog.CheckCancellation();
        if (_done >= _steps)
            return;
        _done++;
        Report(false);
    }

    public void End()
    {
        if (_detail == null)
            return;
        _start += _weight;
        _weight = 0;
        _steps = 0;
        _done = 0;
        Report(true);
        _detail = null;
    }

    private void Report(bool force)
    {
        AnalysisLog.CheckCancellation();
        if (callback == null)
            return;
        var position = _start + (_steps > 0 ? _weight * _done / _steps : 0);
        // The epsilon absorbs rounding when the phase weights are summed in a different order than they were planned.
        var current = _total > 0 ? Math.Min(Scale, (long)(position / _total * Scale + 1e-6)) : 0;
        if (current == _reported && !force)
            return;
        callback(new OperationProgress(label, current, Scale, _detail));
        _reported = current;
    }
}
