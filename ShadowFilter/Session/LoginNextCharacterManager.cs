using Decal.Adapter;
using ShadowFilter.Interop;
using ShadowFilter.Launch;

namespace ShadowFilter.Session;

internal sealed class LoginNextCharacterManager
{
    private readonly LoginCharacterTools _loginCharacterTools;
    private readonly System.Windows.Forms.Timer _timer = new();
    private string? _nextCharacter;
    private int _nextCharByIndex = -1;

    public LoginNextCharacterManager(LoginCharacterTools loginCharacterTools)
    {
        _loginCharacterTools = loginCharacterTools;
        _timer.Interval = 1000;
        _timer.Tick += OnTimerTick;
    }

    public void OnServerDispatch(NetworkMessageEventArgs e)
    {
        if (e.Message.Type == 0xF7E1)
            _timer.Start();
    }

    public void OnCommandLineText(ChatParserInterceptEventArgs e)
    {
        var lower = e.Text.ToLowerInvariant();

        if (lower.StartsWith("/tf lnc set "))
        {
            _nextCharacter = e.Text.Substring(12);
            _nextCharByIndex = -1;
            ChatText.Write("Login Next Character set to: " + _nextCharacter);
            e.Eat = true;
        }
        else if (lower.StartsWith("/tf lncbi set "))
        {
            _nextCharacter = null;
            if (int.TryParse(e.Text.Substring(14), out var index) && index >= 0 && index <= 10)
            {
                _nextCharByIndex = index;
                ChatText.Write("Login Next Character set to index: " + index);
            }
            else
            {
                _nextCharByIndex = -1;
                ChatText.Write("Login Next Character failed: index must be 0-10");
            }

            e.Eat = true;
        }
        else if (lower == "/tf lnc clear" || lower == "/tf lncbi clear")
        {
            _nextCharacter = null;
            _nextCharByIndex = -1;
            ChatText.Write("Login Next Character cleared");
            e.Eat = true;
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            _timer.Stop();

            if (!string.IsNullOrEmpty(_nextCharacter))
            {
                _loginCharacterTools.LoginCharacter(_nextCharacter!);
                _nextCharacter = null;
            }
            else if (_nextCharByIndex >= 0 && _nextCharByIndex <= 10)
            {
                _loginCharacterTools.LoginByIndex(_nextCharByIndex);
                _nextCharByIndex = -1;
            }
        }
        catch
        {
            _timer.Stop();
        }
    }
}
