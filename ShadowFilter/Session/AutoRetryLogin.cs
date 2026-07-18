using Decal.Adapter;
using ShadowFilter.Interop;
using ShadowFilter.Monitoring;

namespace ShadowFilter.Session;

/// <summary>
/// Retries after OrderedDialog (0xF659) login failures — including the common
/// "you can't log on to the same account twice" dialog after a crash.
/// Also marks the heartbeat so the launcher can kill clients stuck on that dialog.
/// Caps attempts so a stuck dialog cannot hammer PostMessage forever.
/// </summary>
internal sealed class AutoRetryLogin
{
    private const int MaxAttempts = 30; // ~6s at 200ms tick

    private readonly System.Windows.Forms.Timer _timer = new();
    private int _state;
    private int _attempts;

    public AutoRetryLogin()
    {
        _timer.Interval = 200;
        _timer.Tick += OnTimerTick;
    }

    public void OnClientDispatch(NetworkMessageEventArgs e)
    {
        // EnterGame request — past the failure dialog.
        if (e.Message.Type == 0xF7C8)
            StopRetry();
    }

    public void OnServerDispatch(NetworkMessageEventArgs e)
    {
        // Character list / world entry — past the failure dialog.
        if (e.Message.Type is 0xF7E1 or 0xF7EA)
        {
            StopRetry();
            return;
        }

        if (e.Message.Type == 0xF659)
        {
            HeartbeatWriter.RecordLoginFailure();
            _state = 0;
            _attempts = 0;
            _timer.Start();
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_attempts >= MaxAttempts)
        {
            PluginLog.Warn(nameof(AutoRetryLogin),
                $"Stopping login retry after {MaxAttempts} attempts — dialog still present");
            _timer.Stop();
            return;
        }

        _attempts++;

        if (_state == 0)
        {
            PostMessageTools.ClickOK();
            _state = 1;
        }
        else
        {
            PostMessageTools.SendMouseClick(0x015C, 0x0185);
            _state = 0;
        }
    }

    private void StopRetry()
    {
        _timer.Stop();
        _attempts = 0;
        HeartbeatWriter.ClearLoginFailure();
    }
}
