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
        if (ParallelFilterGuard.TryMatchChatCommand(e.Text, "lcmq add ", out var lcmq) ||
            ParallelFilterGuard.TryMatchChatCommand(e.Text, "lmq add ", out lcmq))
        {
            _queue.Enqueue(lcmq);
            ChatText.Write("Login Complete Message Queue added: " + e.Text);
            e.Eat = true;
        }
        else if (ParallelFilterGuard.MatchesChatCommand(e.Text, "lcmq clear") ||
                 ParallelFilterGuard.MatchesChatCommand(e.Text, "lmq clear"))
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
        catch (Exception ex)
        {
            OomGuard.DieIfOom(ex);
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
