using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>Runs every dispatcher call immediately, on the calling thread, under one lock (deterministic and safe for shared lists).</summary>
internal sealed class InlineUiDispatcher : IUiDispatcher
{
    private readonly object _gate = new();
    public void Invoke(Action action) { lock (_gate) action(); }
    public void Post(Action action) { lock (_gate) action(); }
    public void Post(UiPriority priority, Action action) { lock (_gate) action(); }
}
