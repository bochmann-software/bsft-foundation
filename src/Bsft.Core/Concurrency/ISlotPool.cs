namespace Bsft.Core.Concurrency;

/// <summary>A fixed number of slots that callers take in the order they asked.</summary>
public interface ISlotPool
{
    /// <summary>Gets how many slots the pool has.</summary>
    int Capacity { get; }

    /// <summary>Gets how many slots are free right now.</summary>
    int Available { get; }

    /// <summary>Waits for a free slot.</summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The held slot; disposing it hands the slot to the longest waiting caller.</returns>
    Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default);
}
