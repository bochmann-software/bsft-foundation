using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Bsft.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bsft.Data.Transactions;

/// <summary>
/// One database transaction per scope, on one connection every enlisted <see cref="DbContext"/>
/// shares: the outermost unit opens and commits it, every unit started inside it joins.
/// </summary>
/// <remarks>
/// <para>
/// A context enlists by being configured with <c>UseBsftSharedTransaction</c>, which hands it this
/// scope's connection, records its type here and adds the interceptor that hands it the open
/// transaction (<c>Database.UseTransaction</c>) as it initializes and before its first command or save.
/// That is what makes a context resolved after the transaction began — the context of a module a
/// nested command reaches — part of it without anybody passing the transaction along.
/// </para>
/// <para>
/// The outermost unit opens the transaction, runs the work, saves whatever an enlisted context still
/// tracks as changed, and commits once. A nested unit only runs its work: committing early would make
/// half of the change durable, and nesting a second transaction on the same connection is refused by
/// the providers. A nested unit that failed marks the transaction for rollback, so an outer handler
/// that swallowed the exception cannot commit what the store has typically already aborted.
/// </para>
/// <para>
/// A unit that does not commit is rolled back by disposing its transaction — which covers every
/// failure path without a catch block that could swallow — and the change trackers of every enlisted
/// context are cleared, because they still describe rows the rollback removed. That is also what lets
/// a retry of the whole outermost unit start from a clean slate. Taking the transaction back from the
/// contexts and disposing it run whatever the other did; after a commit a failure of either is logged
/// rather than thrown, because the unit persisted.
/// </para>
/// <para>
/// The connection is created by the host's factory when this scope first needs it and is disposed with
/// the scope. A unit opens it if it is closed and closes it again afterwards, so it returns to the
/// provider's pool between units.
/// </para>
/// <para>
/// A scope runs one unit of work at a time. The outermost unit is owned by the asynchronous flow that
/// started it: a unit started in that flow — a nested command — joins, and a second outermost unit
/// started concurrently from another flow of the same scope fails with an
/// <see cref="AmbientTransactionMisuseException"/> instead of racing the first for the one connection.
/// </para>
/// </remarks>
internal sealed class SharedTransaction : IAmbientTransaction, IUnitOfWork, IAsyncDisposable, IDisposable
{
    // The refusals of an overlapping unit, which no recorded failure may turn into a conflict. Weak, so a
    // refusal is forgotten with the exception.
    private static readonly ConditionalWeakTable<Exception, object> OverlapRefusals = new();

    private readonly DbConnection _connection;
    private readonly SharedContextProvider _contexts;
    private readonly SharedTransactionHealth _health;
    private readonly SharedTransactionStepReporter _steps;
    private readonly ILogger<SharedTransaction> _logger;
    private readonly IsolationLevel _isolationLevel;
    private readonly AsyncLocal<object?> _flow = new();

    private object? _owner;
    private bool _isRollbackOnly;
    private Exception? _nestedFailure;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharedTransaction"/> class.
    /// </summary>
    /// <param name="connection">The connection every enlisted context of the scope uses; owned by this instance.</param>
    /// <param name="contexts">The scope's contexts taking part in the transaction.</param>
    /// <param name="options">The data options carrying the isolation level.</param>
    /// <param name="health">The providers' view of conflicts and of a transaction the store aborted.</param>
    /// <param name="steps">Tells the scope's observers about the steps run on the connection directly.</param>
    /// <param name="logger">Records a cleanup that failed after the unit committed.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public SharedTransaction(
        DbConnection connection,
        SharedContextProvider contexts,
        IOptions<BsftDataOptions> options,
        SharedTransactionHealth health,
        SharedTransactionStepReporter steps,
        ILogger<SharedTransaction> logger)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(contexts);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(logger);

        _connection = connection;
        _contexts = contexts;
        _health = health;
        _steps = steps;
        _logger = logger;
        _isolationLevel = options.Value.SharedTransaction.IsolationLevel;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Only for the asynchronous flow that owns the open unit and the flows it started: a concurrent
    /// flow of the same scope does not see the unit as its own.
    /// </remarks>
    public bool IsActive => IsOwnedByThisFlow(Volatile.Read(ref _owner));

    /// <inheritdoc />
    /// <remarks>
    /// The first command failure of the last unit counts as well: a handler that swallowed a deadlock
    /// typically fails afterwards with PostgreSQL's "current transaction is aborted", which says nothing
    /// about a conflict, while the swallowed failure does. A failure of a nested unit counts only through
    /// the exception chain: the refused commit carries it as inner exception, and a handler that caught it
    /// and threw an exception of its own decided the outcome itself. A refused overlapping unit is never a
    /// conflict, whatever the unit the other flow runs recorded: it fails the same way again, and a fresh
    /// scope would hide the bug.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="exception"/> is null.</exception>
    public bool IsTransactionConflict(Exception exception) => FindTransactionConflict(exception) is not null;

    /// <inheritdoc />
    /// <remarks>
    /// The same classification as <see cref="IsTransactionConflict"/>, answering with the link of the chain
    /// or the recorded command failure a provider called a conflict.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="exception"/> is null.</exception>
    public Exception? FindTransactionConflict(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return OverlapRefusals.TryGetValue(exception, out _) ? null : _health.FindConflict(exception);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="work"/> is null.</exception>
    /// <exception cref="AmbientTransactionMisuseException">
    /// Thrown when a nested unit failed and the outermost work completed anyway, when the store aborted
    /// the transaction, when an enlisted context retries on failure through its own execution strategy,
    /// or when another asynchronous flow of the scope already runs an outermost unit.
    /// </exception>
    public Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        // The claim is atomic, so of two flows starting an outermost unit at once exactly one wins.
        var claim = new object();
        var owner = Interlocked.CompareExchange(ref _owner, claim, null);

        if (owner is null)
            return RunOutermostAsync(claim, work, cancellationToken);

        return IsOwnedByThisFlow(owner)
            ? JoinAsync(work, cancellationToken)
            : Task.FromException<TResult>(OverlappingUnit());
    }

    /// <inheritdoc />
    /// <remarks>
    /// Saves every enlisted context that tracks a change, as one unit: inside an open unit that is
    /// part of it, outside one it is a unit of its own, so the contexts commit together either way.
    /// </remarks>
    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(async token =>
        {
            await SaveChangedContextsAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => _connection.Dispose();

    /// <summary>
    /// Configures a context of this scope onto the shared connection: the provider is pointed at the
    /// connection, and the context type is recorded so the unit can save and clear it.
    /// </summary>
    /// <param name="optionsBuilder">The options of the context being resolved.</param>
    /// <param name="useProvider">The host's provider call, given the shared connection.</param>
    internal void Configure(
        DbContextOptionsBuilder optionsBuilder, Action<DbContextOptionsBuilder, DbConnection> useProvider)
    {
        useProvider(optionsBuilder, _connection);

        _contexts.Record(optionsBuilder.Options.ContextType);
    }

    /// <summary>
    /// Runs a unit inside the open one: no transaction of its own, no commit. A failure marks the
    /// transaction for rollback and is kept, the first one only, as the cause the outermost unit reports.
    /// </summary>
    private async Task<TResult> JoinAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work, CancellationToken cancellationToken)
    {
        try
        {
            return await work(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _isRollbackOnly = true;
            _nestedFailure ??= exception;

            throw;
        }
    }

    /// <summary>
    /// Runs the unit this flow claimed: opens the transaction, runs the work, saves what is pending,
    /// commits once, and gives the claim back.
    /// </summary>
    private async Task<TResult> RunOutermostAsync<TResult>(
        object owner, Func<CancellationToken, Task<TResult>> work, CancellationToken cancellationToken)
    {
        // Set inside this method, so the marker reaches every unit the work starts, which is how those
        // join, and is gone again for the caller.
        _flow.Value = owner;

        try
        {
            return await RunOnOpenConnectionAsync(work, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _owner, null);
        }
    }

    /// <summary>Opens the connection when it is closed, runs the unit, and closes it again.</summary>
    private async Task<TResult> RunOnOpenConnectionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work, CancellationToken cancellationToken)
    {
        var isOpenedHere = _connection.State != ConnectionState.Open;
        if (isOpenedHere)
        {
            await _steps
                .RunAsync(SharedTransactionStepKind.Open, _connection, _connection.OpenAsync, cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            return await RunInTransactionAsync(work, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (isOpenedHere)
            {
                await _steps
                    .RunEndingAsync(SharedTransactionStepKind.Close, _connection, _ => _connection.CloseAsync())
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Runs one outermost attempt in a transaction of its own on the open connection. Nothing rolls
    /// back explicitly: disposing the transaction rolls back whatever was not committed.
    /// </summary>
    private async Task<TResult> RunInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work, CancellationToken cancellationToken)
    {
        var transaction = await _steps
            .RunAsync(
                SharedTransactionStepKind.Begin,
                _connection,
                token => _connection.BeginTransactionAsync(_isolationLevel, token).AsTask(),
                cancellationToken)
            .ConfigureAwait(false);
        _isRollbackOnly = false;
        _nestedFailure = null;
        var isCommitted = false;

        try
        {
            _contexts.Begin(transaction);

            var result = await work(cancellationToken).ConfigureAwait(false);

            ThrowIfDoomed();

            // Not the caller's token from here on: the work is done, so a caller that cancels now has
            // nothing left to stop. Saving what the contexts still track, asking the store and committing
            // finish the unit; abandoning any of them would throw away completed work, and abandoning the
            // commit halfway would leave the caller unable to tell whether the unit persisted.
            await SaveChangedContextsAsync(CancellationToken.None).ConfigureAwait(false);
            if (_health.HasAbortProbes)
            {
                await _steps
                    .RunAsync(
                        SharedTransactionStepKind.AbortProbe,
                        _connection,
                        token => _health.ThrowIfAbortedAsync(transaction, token),
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            await _steps
                .RunAsync(SharedTransactionStepKind.Commit, _connection, transaction.CommitAsync, CancellationToken.None)
                .ConfigureAwait(false);
            isCommitted = true;

            return result;
        }
        finally
        {
            // The nested failure belongs to this unit; the refused commit already carries it.
            _nestedFailure = null;

            if (isCommitted)
                await EndCommittedAsync(transaction).ConfigureAwait(false);
            else
                await EndRolledBackAsync(transaction).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Ends a committed unit. A failure of the cleanup is logged instead of thrown: the unit persisted,
    /// and reporting it as failed would make a caller, or a retry, write it again.
    /// </summary>
    private async Task EndCommittedAsync(DbTransaction transaction)
    {
        try
        {
            await ReleaseAsync(transaction, SharedTransactionStepKind.Release).ConfigureAwait(false);
        }
        catch (DbException exception)
        {
            LogFailedCleanup(exception);
        }
        catch (InvalidOperationException exception)
        {
            LogFailedCleanup(exception);
        }
    }

    /// <summary>
    /// Ends a unit that did not commit: forgets what the change trackers still describe, then releases
    /// the transaction, which disposing rolls back.
    /// </summary>
    private async Task EndRolledBackAsync(DbTransaction transaction)
    {
        try
        {
            _contexts.ClearChangeTrackers();
        }
        finally
        {
            await ReleaseAsync(transaction, SharedTransactionStepKind.Rollback).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Takes the ended transaction back from the contexts and disposes it, whatever the first did, as the
    /// step <paramref name="kind"/>.
    /// </summary>
    private async Task ReleaseAsync(DbTransaction transaction, SharedTransactionStepKind kind)
    {
        try
        {
            _contexts.Release(transaction);
        }
        finally
        {
            await _steps
                .RunEndingAsync(kind, _connection, _ => transaction.DisposeAsync().AsTask())
                .ConfigureAwait(false);
        }
    }

    private void LogFailedCleanup(Exception exception) =>
        _logger.LogWarning(
            exception,
            "The shared transaction committed, but taking it back from its contexts or disposing it failed afterwards; the unit persisted.");

    /// <summary>
    /// Throws when the transaction cannot commit: a nested unit failed, or a command failed or was
    /// cancelled, and the outer work completed anyway because the failure was caught. The first
    /// command failure, or else the first failure of a nested unit, is the inner exception, so a
    /// provider's conflict classification still sees it.
    /// </summary>
    private void ThrowIfDoomed()
    {
        _health.ThrowIfCommandFailed();

        if (_isRollbackOnly)
        {
            const string message =
                "A unit nested in the shared transaction failed, so the transaction cannot commit. The outer work completed anyway, which means the failure was caught and not rethrown; the whole transaction is rolled back instead.";

            throw _nestedFailure is { } nestedFailure
                ? new AmbientTransactionMisuseException(message, nestedFailure)
                : new AmbientTransactionMisuseException(message);
        }
    }

    /// <summary>Reports whether <paramref name="owner"/> is the unit this asynchronous flow runs.</summary>
    private bool IsOwnedByThisFlow(object? owner) => owner is not null && ReferenceEquals(_flow.Value, owner);

    /// <summary>Reports a second outermost unit started while another flow of the scope runs one.</summary>
    private static AmbientTransactionMisuseException OverlappingUnit()
    {
        var refusal = new AmbientTransactionMisuseException(
            "Another asynchronous flow of this scope already runs a unit of the shared transaction. A scope runs one unit of work at a time: start concurrent units in scopes of their own, or await the running unit first.");
        OverlapRefusals.AddOrUpdate(refusal, refusal);

        return refusal;
    }

    /// <summary>Saves every context of the scope that tracks a change.</summary>
    private async Task SaveChangedContextsAsync(CancellationToken cancellationToken)
    {
        foreach (var context in _contexts.GetContexts())
        {
            if (context.ChangeTracker.HasChanges())
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

}
