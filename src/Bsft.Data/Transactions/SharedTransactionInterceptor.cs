using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bsft.Data.Transactions;

/// <summary>
/// Hands the scope's open shared transaction to a context: as the context initializes, and again before
/// EF Core creates a command and before a save decides whether to open a transaction of its own.
/// </summary>
/// <remarks>
/// <para>
/// A context resolved after the transaction began — a module's context that a nested command reaches
/// first — takes it as it initializes (<see cref="SharedContextLifetimeListener"/>), so code that asks
/// <c>Database.CurrentTransaction</c> before its first command already finds the shared one and joins
/// it rather than beginning a transaction of its own. The command and save hooks are the fallback for
/// a context that initialized before the unit began. The save hook is needed besides the command hook
/// because <c>SaveChanges</c> opens its own transaction before it creates a command, when the context
/// has none; given the shared one first, it uses that and commits nothing itself.
/// </para>
/// <para>
/// A command that fails or is cancelled while the transaction is open is recorded, because the store
/// has typically aborted the transaction with it: PostgreSQL then answers <c>COMMIT</c> with
/// <c>ROLLBACK</c> and Npgsql reports no error, so a handler that swallowed the exception would see
/// success for writes that were never persisted. The unit fails instead, with the recorded failure as
/// the inner exception.
/// </para>
/// <para>
/// A command that fails inside <c>SaveChanges</c> is the exception when EF Core protects the save with a
/// savepoint — automatic savepoints enabled and supported by the transaction, which is the default for
/// a context saving inside a transaction it did not open. EF Core rolls back to that savepoint before it
/// rethrows, so the transaction stays able to commit, and a handler that catches the failure — a unique
/// violation it answers differently — can go on and commit the rest. Whatever the store aborted anyway
/// is caught by the provider's <see cref="ITransactionAbortProbe"/> before the commit.
/// </para>
/// <para>
/// One instance per scope, added to the options of every enlisted context of that scope.
/// </para>
/// </remarks>
internal sealed class SharedTransactionInterceptor : DbCommandInterceptor, ISaveChangesInterceptor
{
    private readonly SharedTransactionEnlistment _enlistment;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharedTransactionInterceptor"/> class.
    /// </summary>
    /// <param name="enlistment">The scope's open shared transaction and the contexts using it.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="enlistment"/> is null.</exception>
    public SharedTransactionInterceptor(SharedTransactionEnlistment enlistment) =>
        _enlistment = enlistment ?? throw new ArgumentNullException(nameof(enlistment));

    /// <inheritdoc />
    public override InterceptionResult<DbCommand> CommandCreating(
        CommandCorrelatedEventData eventData, InterceptionResult<DbCommand> result)
    {
        Enlist(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Enlist(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Enlist(eventData.Context);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        if (!IsProtectedBySavepoint(eventData))
            _enlistment.RecordFailure(eventData.Exception);
    }

    /// <inheritdoc />
    public override Task CommandFailedAsync(
        DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        CommandFailed(command, eventData);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override void CommandCanceled(DbCommand command, CommandEndEventData eventData) =>
        _enlistment.RecordFailure(new OperationCanceledException("A database command was cancelled inside the shared transaction."));

    /// <inheritdoc />
    public override Task CommandCanceledAsync(
        DbCommand command, CommandEndEventData eventData, CancellationToken cancellationToken = default)
    {
        CommandCanceled(command, eventData);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reports whether a failed command belongs to a save EF Core rolls back to its savepoint, which
    /// leaves the transaction able to commit.
    /// </summary>
    private static bool IsProtectedBySavepoint(CommandErrorEventData eventData) =>
        eventData.CommandSource == CommandSource.SaveChanges
        && eventData.Context is { Database: var database }
        && database.AutoSavepointsEnabled
        && database.CurrentTransaction?.SupportsSavepoints == true;

    /// <summary>
    /// Hands the open transaction to a context of this scope as it initializes, before any code can
    /// ask it for its current transaction.
    /// </summary>
    /// <param name="context">The context that initialized.</param>
    internal void EnlistInitialized(DbContext context) => _enlistment.Enlist(context);

    /// <summary>Forgets a context of this scope that was disposed.</summary>
    /// <param name="context">The disposed context.</param>
    internal void ForgetDisposed(DbContext context) => _enlistment.Forget(context);

    /// <summary>Enlists the context of an event, when the event has one.</summary>
    private void Enlist(DbContext? context)
    {
        if (context is not null)
            _enlistment.Enlist(context);
    }
}
