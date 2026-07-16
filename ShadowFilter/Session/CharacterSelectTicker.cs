using ShadowFilter.Interop;

namespace ShadowFilter.Session;

/// <summary>
/// Shared 4-tick char-select timer: ticks 1-2 click (350,100), tick 3 invokes login callback.
/// </summary>
internal sealed class CharacterSelectTicker
{
    private readonly System.Windows.Forms.Timer _timer = new();
    private int _state;
    private Action? _onLogin;

    public CharacterSelectTicker()
    {
        _timer.Interval = 1000;
        _timer.Tick += OnTimerTick;
    }

    public void Start(Action onLogin)
    {
        _state = 0;
        _onLogin = onLogin;
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            if (_state == 1 || _state == 2)
                PostMessageTools.SendMouseClick(350, 100);

            if (_state == 3)
                _onLogin?.Invoke();

            if (_state >= 3)
                _timer.Stop();

            _state++;
        }
        catch (Exception ex)
        {
            PluginLog.Error(nameof(CharacterSelectTicker), "Ticker tick failed", ex);
            _timer.Stop();
        }
    }
}
