using Decal.Adapter;
using ShadowFilter.Interop;
using ShadowFilter.Monitoring;

namespace ShadowFilter.Session;

/// <summary>
/// Retries after OrderedDialog (0xF659) login failures — including the common
/// "you can't log on to the same account twice" dialog after a crash.
/// Also marks the heartbeat so the launcher can kill clients stuck on that dialog.
/// </summary>
internal sealed class AutoRetryLogin
{
    private readonly System.Windows.Forms.Timer _timer = new();
    private int _state;

    public AutoRetryLogin()
    {
        _timer.Interval = 200;
        _timer.Tick += OnTimerTick;
    }

    public void OnClientDispatch(NetworkMessageEventArgs e)
    {
        // EnterGame request — past the failure dialog.
        if (e.Message.Type == 0xF7C8)
        {
            _timer.Stop();
            HeartbeatWriter.ClearLoginFailure();
        }
    }

    public void OnServerDispatch(NetworkMessageEventArgs e)
    {
        // Character list / world entry — past the failure dialog.
        if (e.Message.Type is 0xF7E1 or 0xF7EA)
        {
            _timer.Stop();
            HeartbeatWriter.ClearLoginFailure();
            return;
        }

        if (e.Message.Type == 0xF659)
        {
            HeartbeatWriter.RecordLoginFailure();
            _state = 0;
            _timer.Start();
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
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
}
