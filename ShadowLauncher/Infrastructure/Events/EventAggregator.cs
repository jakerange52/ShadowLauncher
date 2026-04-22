using System.Collections.Concurrent;
using ShadowLauncher.Core.Interfaces;

namespace ShadowLauncher.Infrastructure.Events;

public sealed class EventAggregator : IEventAggregator
{
    private readonly ConcurrentDictionary<Type, List<object>> _subscribers = new();

    public void Publish<TEvent>(TEvent eventData) where TEvent : class
    {
        if (!_subscribers.TryGetValue(typeof(TEvent), out var handlers))
            return;

        List<Action<TEvent>> snapshot;
        lock (handlers)
        {
            snapshot = handlers.Cast<Action<TEvent>>().ToList();
        }

        foreach (var handler in snapshot)
        {
            handler(eventData);
        }
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        var handlers = _subscribers.GetOrAdd(typeof(TEvent), _ => []);
        lock (handlers)
        {
            handlers.Add(handler);
        }
        return new Subscription(() =>
        {
            lock (handlers)
            {
                handlers.Remove(handler);
            }
        });
    }

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        private Action? _onDispose = onDispose;
        public void Dispose() => Interlocked.Exchange(ref _onDispose, null)?.Invoke();
    }
}
