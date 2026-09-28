using Bsft.Data.Contracts.Pagination;

namespace Bsft.EventAggregator.Saga.Reading;

/// <summary>
/// Reads saga instances across every state type, for an operator rather than for the engine.
/// </summary>
/// <remarks>
/// Not <see cref="ISagaStore"/>, which addresses one instance by type and correlation
/// key because that is all a running saga ever needs. This one lists, which is what makes a stalled
/// instance visible without knowing its key in advance.
/// </remarks>
public interface ISagaInstanceReader
{
    /// <summary>Reads one page of saga metadata.</summary>
    /// <param name="filter">What to narrow by, or <see langword="null"/> for every instance.</param>
    /// <param name="order">How to order the page.</param>
    /// <param name="request">The page to read.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The page.</returns>
    Task<PagedResult<SagaInstanceSummary>> ListAsync(
        SagaInstanceFilter? filter,
        SagaInstanceOrder order,
        PagedRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one saga instance with the state it holds.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The instance, or <see langword="null"/> when there is no such row.</returns>
    Task<SagaInstanceView?> FindAsync(int id, CancellationToken cancellationToken = default);
}
