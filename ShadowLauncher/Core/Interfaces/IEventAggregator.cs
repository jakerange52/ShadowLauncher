namespace ShadowLauncher.Core.Interfaces;

public interface IEventAggregator
{
    void Publish<TEvent>(TEvent eventData) where TEvent : class;
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
}
