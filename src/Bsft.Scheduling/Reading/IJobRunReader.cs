using Bsft.Data.Contracts.Pagination;

namespace Bsft.Scheduling.Reading;

/// <summary>
/// The operator read side of the job run table: what ran, what failed and what is still owed.
/// </summary>
/// <remarks>
/// It is a seam of its own rather than more methods on <see cref="IJobStore"/>, because that contract
/// is the leased queue the dispatcher drains and every host that schedules work has to implement all
/// of it. This is what a console reads, it answers a paged question the dispatcher never asks, and a
/// host is free to serve it from a replica.
/// <para>
/// The in-memory store ships no implementation. A console asking what failed last night is asking
/// about runs that outlive the process, and the in-memory store exists for tests and a single
/// instance.
/// </para>
/// </remarks>
public interface IJobRunReader
{
    /// <summary>
    /// Lists the runs matching a filter.
    /// </summary>
    /// <param name="filter">Which runs to list, or null for all of them.</param>
    /// <param name="order">How to order the page.</param>
    /// <param name="request">Which page to answer with.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The page, with the total the filter matched.</returns>
    Task<PagedResult<JobRunSummary>> ListAsync(
        JobRunFilter? filter,
        JobRunOrder order,
        PagedRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one run together with its arguments.
    /// </summary>
    /// <param name="runId">The run to read.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The run, or null when there is none.</returns>
    Task<JobRunView?> FindAsync(long runId, CancellationToken cancellationToken = default);
}
