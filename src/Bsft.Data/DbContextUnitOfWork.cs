using Bsft.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Data;

/// <summary>
/// The framework's unit of work over the host's own <see cref="DbContext"/>: a commit is that
/// context's save, and a unit is one transaction on that context's connection.
/// </summary>
/// <typeparam name="TContext">The application's context, whose connection the transaction is opened on.</typeparam>
/// <remarks>
/// <para>
/// It is the host's context on purpose. Every package in this framework that persists through EF Core
/// writes into the application's context rather than one of its own, so that a row it stages commits
/// with whatever else the request was writing; a unit of work over a second context would commit half
/// of that and call it durable. This class therefore adds no storage of its own — it is the shared
/// implementation of the two contracts in <c>Bsft.Core</c> for the storage a host already has.
/// </para>
/// <para>
/// A transaction the host already opened is joined rather than nested: a second transaction on the
/// same connection would either fail outright or quietly commit one half on its own. Where the
/// provider is not relational — the in-memory provider a host meters with — the work simply runs, for
/// the same reason the in-memory stores state about themselves: there is nothing to roll back to, and
/// pretending otherwise would be a guarantee this class cannot keep.
/// </para>
/// <para>
/// Otherwise the unit runs through the provider's execution strategy. A host that configured retries
/// would otherwise get an exception the moment a transaction was opened: EF Core refuses a
/// user-initiated transaction under a retrying strategy unless the whole unit is handed to the
/// strategy, precisely because a retry has to re-run the work from the start rather than resume it
/// half-committed.
/// </para>
/// </remarks>
public sealed class DbContextUnitOfWork<TContext> : IUnitOfWork, ITransactionalUnitOfWork
    where TContext : DbContext
{
    private readonly TContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="DbContextUnitOfWork{TContext}"/> class.
    /// </summary>
    /// <param name="dbContext">The application's context this unit of work commits.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dbContext"/> is null.</exception>
    public DbContextUnitOfWork(TContext dbContext) =>
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken = default) => _dbContext.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="work"/> is null.</exception>
    public Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (!_dbContext.Database.IsRelational() || _dbContext.Database.CurrentTransaction is not null)
            return work(cancellationToken);

        return _dbContext.Database
            .CreateExecutionStrategy()
            .ExecuteAsync(work, (state, token) => RunInTransactionAsync(state, token), cancellationToken);
    }

    /// <summary>Runs one attempt of the work inside a transaction and commits it.</summary>
    /// <typeparam name="TResult">What the work answers.</typeparam>
    /// <param name="work">The writes to make durable together.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>What the work answered.</returns>
    /// <remarks>
    /// Nothing rolls back explicitly: disposing a transaction that was never committed rolls it back,
    /// which covers the failure this exists for — an exception on any path, named or not — without a
    /// catch block that would have to rethrow and could swallow.
    /// </remarks>
    private async Task<TResult> RunInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = await work(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return result;
    }
}
