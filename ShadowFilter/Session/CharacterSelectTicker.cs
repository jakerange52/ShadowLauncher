using ShadowFilter.Interop;

namespace ShadowFilter.Session;

/// <summary>
/// Clicks (350,100) to dismiss intro movies, then clicks the character.
/// One login click is not enough when a movie is still up: that click skips the
/// movie and the character screen never receives it. Login is retried until the
/// client sends EnterGame (0xF7C8) or the attempt cap is reached.
/// </summary>
internal sealed class CharacterSelectTicker
{
    private const int SkipSeconds = 5;
    private const int LoginGapSeconds = 3;
    private const int MaxLoginAttempts = 4;

    private readonly System.Windows.Forms.Timer _timer = new();
    private int _state;
    private int _loginAttempts;
    private bool _enteredWorld;
    private Action? _onLogin;

    public CharacterSelectTicker()
    {
        _timer.Interval = 1000;
        _timer.Tick += OnTimerTick;
    }

    public void Start(Action onLogin)
    {
        _state = 0;
        _loginAttempts = 0;
        _enteredWorld = false;
        _onLogin = onLogin;
        _timer.Start();
    }

    public void NotifyEnteredWorld()
    {
        _enteredWorld = true;
        _timer.Stop();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            if (_enteredWorld)
            {
                _timer.Stop();
                return;
            }

            // Tick 0 is a one-second settle. The next SkipSeconds ticks dismiss movies.
            // The character click follows, then repeats until EnterGame.
            if (_state >= 1 && _state <= SkipSeconds)
            {
                PluginLog.Info(nameof(CharacterSelectTicker), $"Intro skip click {_state}");
                PostMessageTools.SendMouseClick(350, 100);
            }
            else if (_state >= SkipSeconds + 1 && (_state - (SkipSeconds + 1)) % LoginGapSeconds == 0)
            {
                if (_loginAttempts >= MaxLoginAttempts)
                {
                    PluginLog.Warn(nameof(CharacterSelectTicker), "Character click gave up before EnterGame");
                    _timer.Stop();
                    return;
                }

                _loginAttempts++;
                PluginLog.Info(nameof(CharacterSelectTicker), $"Character click attempt {_loginAttempts}");
                _onLogin?.Invoke();
            }

            _state++;
        }
        catch (Exception ex)
        {
            OomGuard.DieIfOom(ex);
            PluginLog.Error(nameof(CharacterSelectTicker), "Ticker tick failed", ex);
            _timer.Stop();
        }
    }
}
