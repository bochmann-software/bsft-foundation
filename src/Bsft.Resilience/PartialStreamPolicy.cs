namespace Bsft.Resilience;

/// <summary>
/// What <see cref="ResilientStreamExtensions"/> does when a stream fails <em>after</em> it has already
/// handed items to the consumer. The choice is the caller's because the framework cannot know whether
/// the consumer can tolerate seeing an item twice.
/// </summary>
/// <remarks>
/// A request/response retry is invisible: the failed attempt produced nothing the caller saw. A stream
/// retry is not. Once an item has been yielded it is gone downstream — written to a socket, appended to
/// a buffer, rendered — and re-invoking the factory produces the stream from its beginning again. There
/// is no third option the framework could pick on its own, so the two are named here and the default is
/// the one that cannot surprise anybody.
/// </remarks>
public enum PartialStreamPolicy
{
    /// <summary>
    /// Retry only while nothing has been yielded yet; a failure after the first item is rethrown to the
    /// consumer. This is the default: no item is ever produced twice.
    /// </summary>
    RethrowMidStream = 0,

    /// <summary>
    /// Retry a mid-stream failure by re-invoking the factory, which starts the stream over. Every item
    /// already yielded is produced <em>again</em> unless the factory resumes from
    /// <see cref="StreamRetryContext.ItemsYielded"/>. Choose this only for a consumer that either
    /// tolerates duplicates or uses that offset.
    /// </summary>
    RestartFromStart = 1,
}
