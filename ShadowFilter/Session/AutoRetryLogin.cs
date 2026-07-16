using Decal.Adapter;
using ShadowFilter.Interop;

namespace ShadowFilter.Session;

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
        if (e.Message.Type == 0xF7C8)
            _timer.Stop();
    }

    public void OnServerDispatch(NetworkMessageEventArgs e)
    {
        if (e.Message.Type == 0xF659)
        {
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
