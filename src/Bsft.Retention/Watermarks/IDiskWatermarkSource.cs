namespace Bsft.Retention.Watermarks;

/// <summary>
/// The part of a disk watermark an application owns: what its archive holds, oldest first, and how
/// one item of it is deleted.
/// </summary>
/// <remarks>
/// <para>
/// Everything else — measuring the volume, deciding whether the watermark is crossed, working out how
/// much has to go, the hysteresis that keeps the archive from flapping around its limit, the batching
/// and the loop — is the framework's, supplied by <c>AddBsftRetentionDiskWatermark</c>.
/// </para>
/// <para>
/// An implementation is resolved from a fresh dependency injection scope for every sweep, so it may
/// take scoped dependencies such as a <c>DbContext</c>. Within one sweep the enumeration is read to its
/// end before the first <see cref="DeleteAsync"/> call, so the two never overlap on one context.
/// </para>
/// </remarks>
public interface IDiskWatermarkSource
{
    /// <summary>
    /// Lists every item the archive holds, oldest first, with its size.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe. It is the host stopping.</param>
    /// <returns>
    /// The archive's items, oldest first. The sizes of all of them make up the archive's size, so an
    /// item already deleted must not be listed.
    /// </returns>
    /// <remarks>
    /// The order is the eviction order: the pass deletes from the front of this sequence until the
    /// watermark's target holds. An item created at or after the sweep's cutoff is counted towards the
    /// archive's size but never deleted.
    /// </remarks>
    IAsyncEnumerable<DiskWatermarkItemInfo> ListOldestFirstAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes one item the pass chose for eviction, for example by marking its row deleted and
    /// removing its file.
    /// </summary>
    /// <param name="item">The item, as <see cref="ListOldestFirstAsync"/> listed it.</param>
    /// <param name="cancellationToken">A cancellation token to observe. It is the host stopping.</param>
    /// <returns>A task that completes when the item is gone.</returns>
    /// <remarks>
    /// A failure ends the sweep; it is logged and the next sweep measures again and continues. An
    /// implementation must therefore be safe to call again for an item it deleted only in part.
    /// </remarks>
    Task DeleteAsync(DiskWatermarkItemInfo item, CancellationToken cancellationToken = default);
}
