using YTDLPHost.ViewModels;

namespace YTDLPHost.Native
{
    /// <summary>What happened to a browser request once the app tried to queue it.</summary>
    /// <param name="Items">The list items that now stand for the request (new items, or items that were already downloading the same thing). Empty when nothing could be queued.</param>
    /// <param name="Error">Why the request (or the rest of it) could not be queued; null when everything was.</param>
    public sealed record NativeSubmission(IReadOnlyList<DownloadItemViewModel> Items, string? Error);

    /// <summary>
    /// The app side of a browser request (Phase 2, step 2.3): takes downloads that <see cref="DownloadRequestMapper"/> already built and
    /// puts them in the download list. Implemented in Core by <see cref="NativeDownloadSink"/>; tests use a fake.
    /// </summary>
    public interface INativeDownloadSink
    {
        /// <summary>Queues the downloads. Safe to call from any thread.</summary>
        NativeSubmission Submit(IReadOnlyList<MappedDownload> downloads);

        /// <summary>The ids of the items currently in the list, so a watcher can tell that the user removed one. Safe to call from any thread.</summary>
        HashSet<Guid> ListedIds();
    }
}
