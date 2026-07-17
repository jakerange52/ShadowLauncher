using Decal.Adapter;
using ShadowFilter.Interop;

namespace ShadowFilter.Session;

internal sealed class LoginCompleteMessageQueueManager
{
    private bool _freshLogin;
    private readonly Queue<string> _queue = new();
    private bool _sendingLastEnter;
    private bool _renderHooked;

    public void OnClientDispatch(NetworkMessageEventArgs e)
    {
        if (e.Message.Type == 0xF7C8)
        {
            _freshLogin = true;
            Monitoring.HeartbeatWriter.Launch();
        }

        if (!_freshLogin ||
            e.Message.Type != 0xF7B1 ||
            Convert.ToInt32(e.Message["action"]) != 0xA1)
        {
            return;
        }

        _freshLogin = false;

        if (_queue.Count == 0)
            return;

        _sendingLastEnter = false;
        Attach();
    }

    public void OnCommandLineText(ChatParserInterceptEventArgs e)
    {
        if (e.Text.StartsWith("/tf lcmq add ", StringComparison.OrdinalIgnoreCase))
        {
            _queue.Enqueue(e.Text.Substring(13));
            ChatText.Write("Login Complete Message Queue added: " + e.Text);
            e.Eat = true;
        }
        else if (e.Text.StartsWith("/tf lmq add ", StringComparison.OrdinalIgnoreCase))
        {
            _queue.Enqueue(e.Text.Substring(12));
            ChatText.Write("Login Complete Message Queue added: " + e.Text);
            e.Eat = true;
        }
        else if (string.Equals(e.Text, "/tf lcmq clear", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(e.Text, "/tf lmq clear", StringComparison.OrdinalIgnoreCase))
        {
            _queue.Clear();
            ChatText.Write("Login Complete Message Queue cleared");
            e.Eat = true;
        }
    }

    private void OnRenderFrame(object sender, EventArgs e)
    {
        try
        {
            if (_queue.Count == 0 && !_sendingLastEnter)
            {
                Detach();
                return;
            }

            if (_sendingLastEnter)
            {
                PostMessageTools.SendEnter();
                _sendingLastEnter = false;
            }
            else
            {
                PostMessageTools.SendEnter();
                var cmd = _queue.Dequeue();
                PostMessageTools.SendMsg(cmd);
                _sendingLastEnter = true;
            }
        }
        catch
        {
            Detach();
        }
    }

    private void Attach()
    {
        if (_renderHooked)
            return;

        CoreManager.Current.RenderFrame += OnRenderFrame;
        _renderHooked = true;
    }

    private void Detach()
    {
        if (!_renderHooked)
            return;

        CoreManager.Current.RenderFrame -= OnRenderFrame;
        _renderHooked = false;
    }
}
