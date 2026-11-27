using System.Runtime.CompilerServices;
using Bsft.Retention.Watermarks;

namespace Bsft.Retention.Tests.Fakes;

/// <summary>
/// An archive held in a list: it lists what is left oldest first and deletes by removing, so a
/// watermark test asserts against an archive that shrinks the way a real one does.
/// </summary>
internal sealed class FakeDiskWatermarkSource : IDiskWatermarkSource
{
    private readonly List<DiskWatermarkItemInfo> _items = [];
    private readonly List<DiskWatermarkItemInfo> _deleted = [];

    /// <summary>Gets the items still in the archive, in listing order.</summary>
    public IReadOnlyList<DiskWatermarkItemInfo> Items => _items;

    /// <summary>Gets the items deleted so far, in order.</summary>
    public IReadOnlyList<DiskWatermarkItemInfo> Deleted => _deleted;

    /// <summary>Gets or sets a failure the listing throws instead of listing.</summary>
    public Exception? ListFailure { get; set; }

    /// <summary>Gets or sets a failure every deletion throws instead of deleting.</summary>
    public Exception? DeleteFailure { get; set; }

    /// <summary>Gets or sets an action run after each deletion, for example to cancel the sweep.</summary>
    public Action<DiskWatermarkItemInfo>? AfterDelete { get; set; }

    /// <summary>Gets how often the archive was listed.</summary>
    public int Listings { get; private set; }

    /// <summary>Appends an item, which makes it the newest.</summary>
    /// <param name="key">The item's key.</param>
    /// <param name="bytes">The item's size.</param>
    /// <param name="createdAt">When the item was created.</param>
    /// <returns>This source, for chaining.</returns>
    public FakeDiskWatermarkSource Add(string key, long bytes, DateTimeOffset createdAt)
    {
        _items.Add(new DiskWatermarkItemInfo(key, bytes, createdAt));

        return this;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<DiskWatermarkItemInfo> ListOldestFirstAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Listings++;

        if (ListFailure is not null)
            throw ListFailure;

        foreach (var item in _items.ToArray())
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            yield return item;
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(DiskWatermarkItemInfo item, CancellationToken cancellationToken = default)
    {
        if (DeleteFailure is not null)
            throw DeleteFailure;

        _items.Remove(item);
        _deleted.Add(item);
        AfterDelete?.Invoke(item);

        return Task.CompletedTask;
    }
}
