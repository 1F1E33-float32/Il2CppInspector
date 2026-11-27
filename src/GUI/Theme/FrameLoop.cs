using Avalonia.Controls;

namespace Il2CppInspector.GUI.Theme;

// One outstanding animation callback per control; detaching stops it without retaining a closed window.
internal sealed class FrameLoop
{
    private readonly Control _owner;
    private readonly Func<bool> _update;
    private bool _running;
    private bool _pending;

    internal FrameLoop(Control owner, Func<bool> update)
    {
        _owner = owner;
        _update = update;
        owner.DetachedFromVisualTree += (_, _) => _running = false;
    }

    internal void Start()
    {
        _running = true;
        RequestFrame();
    }

    private void RequestFrame()
    {
        if (!_pending && TopLevel.GetTopLevel(_owner) is { } window)
        {
            _pending = true;
            window.RequestAnimationFrame(OnFrame);
        }
    }

    private void OnFrame(TimeSpan elapsed)
    {
        _pending = false;
        if (!_running || TopLevel.GetTopLevel(_owner) is null)
            return;
        _running = _update();
        if (_running)
            RequestFrame();
    }
}
