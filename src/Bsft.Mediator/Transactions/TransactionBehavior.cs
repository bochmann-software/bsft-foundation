using Bsft.Core.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace Bsft.Mediator.Transactions;

/// <summary>
/// Pipeline behavior that wraps command/query execution in a database transaction
/// when the handler is decorated with the <see cref="TransactionalAttribute"/>.
/// Resolves a DbContext from the service provider to manage the transaction.
/// </summary>
/// <remarks>
/// When the host registered an <see cref="IAmbientTransaction"/> — the shared transaction across
/// several contexts of <c>Bsft.Data</c> — the request runs as a unit of that instead: the outermost
/// request opens and commits it, a request dispatched from inside a handler joins it, and no
/// <c>DbContext</c> is resolved here at all.
/// <para>
/// With <see cref="MediatorConfiguration.ApplyTransactionToAllCommands"/> every command is
/// transactional whether or not its handler carries the attribute; queries still need it. A command
/// dispatched while this behavior already holds a transaction open in the same scope and flow
/// (<see cref="IRequestNesting"/>) then runs inside that transaction instead of beginning a second one
/// on the same context. A command dispatched in another scope is the outermost of its own.
/// </para>
/// <para>
/// The ambient transaction is resolved from the scope the request runs in, never taken in the
/// constructor, the same way the retry behavior resolves it: a retry attempt that runs in a fresh scope
/// then reaches the fresh scope's transaction.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The type of the request.</typeparam>
/// <typeparam name="TResponse">The type of the response.</typeparam>
public sealed class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;
    private readonly Predicate<Type> _isTransactionalByPolicy;
    private readonly IRequestNesting _nesting;
    private readonly bool _isAppliedToAllCommands;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionBehavior{TRequest, TResponse}"/> class
    /// without the transaction policy and the scope's nesting marks.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve the DbContext.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <remarks>
    /// Kept for compatibility with code that constructs the behavior itself: only handlers marked
    /// <see cref="TransactionalAttribute"/> are transactional, and every request counts as the outermost
    /// of its scope (<see cref="NullRequestNesting"/>). The container picks the other constructor.
    /// </remarks>
    public TransactionBehavior(IServiceProvider serviceProvider, ILogger<TransactionBehavior<TRequest, TResponse>> logger)
        : this(serviceProvider, logger, static _ => false, NullRequestNesting.Instance, isAppliedToAllCommands: false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionBehavior{TRequest, TResponse}"/> class
    /// that follows the mediator's transaction policy and knows the requests nested in the scope. The
    /// container picks it.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve the DbContext.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="transactionPolicy">The mediator transaction policy.</param>
    /// <param name="metadataProvider">Says which requests are commands.</param>
    /// <param name="nesting">The scope's marks of the requests a transaction is open around.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public TransactionBehavior(
        IServiceProvider serviceProvider,
        ILogger<TransactionBehavior<TRequest, TResponse>> logger,
        IMediatorTransactionPolicy transactionPolicy,
        IRequestMetadataProvider metadataProvider,
        IRequestNesting nesting)
        : this(
            serviceProvider,
            logger,
            CommandsByPolicy(transactionPolicy, metadataProvider),
            nesting ?? throw new ArgumentNullException(nameof(nesting)),
            transactionPolicy.ShouldApplyTransactionToAllCommands)
    {
    }

    private TransactionBehavior(
        IServiceProvider serviceProvider,
        ILogger<TransactionBehavior<TRequest, TResponse>> logger,
        Predicate<Type> isTransactionalByPolicy,
        IRequestNesting nesting,
        bool isAppliedToAllCommands)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _isTransactionalByPolicy = isTransactionalByPolicy;
        _nesting = nesting;
        _isAppliedToAllCommands = isAppliedToAllCommands;
    }

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken = default)
    {
        // The runtime type, not typeof(TRequest): the mediator closes the pipeline over the declared
        // contract - SendAsync takes an ICommand<TResult> - so looking for a handler of the type
        // parameter finds none on the dispatch path and the behavior never opens a transaction. A
        // request dispatched as null cannot name a handler, so the type parameter is all there is.
        var requestType = request?.GetType() ?? typeof(TRequest);

        if (!IsTransactional(requestType))
            return await next().ConfigureAwait(false);

        var requestName = requestType.Name;

        var ambientTransaction = _serviceProvider.GetService<IAmbientTransaction>();
        if (ambientTransaction is not null)
            return await RunInAmbientTransactionAsync(ambientTransaction, next, requestName, cancellationToken).ConfigureAwait(false);

        if (_isAppliedToAllCommands && _nesting.IsTransactionOpen)
            return await next().ConfigureAwait(false);

        var transaction = await BeginDbTransactionOrNullAsync(requestName, cancellationToken).ConfigureAwait(false);
        if (transaction is null)
            return await next().ConfigureAwait(false);

        return await ExecuteInTransactionAsync(transaction, next, requestName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Says which requests the policy makes transactional: every command when the mediator applies the
    /// transaction to all commands, none otherwise.
    /// </summary>
    private static Predicate<Type> CommandsByPolicy(
        IMediatorTransactionPolicy transactionPolicy, IRequestMetadataProvider metadataProvider)
    {
        ArgumentNullException.ThrowIfNull(transactionPolicy);
        ArgumentNullException.ThrowIfNull(metadataProvider);

        return transactionPolicy.ShouldApplyTransactionToAllCommands
            ? requestType => metadataProvider.GetMetadata(requestType).IsCommand
            : static _ => false;
    }

    private bool IsTransactional(Type requestType) =>
        TransactionalHandlers.IsRequired(requestType) || _isTransactionalByPolicy(requestType);

    private Task<TResponse> RunInAmbientTransactionAsync(
        IAmbientTransaction ambientTransaction, Func<Task<TResponse>> next, string requestName, CancellationToken cancellationToken)
    {
        if (ambientTransaction.IsActive)
            _logger.LogInformation("[Transactional] Joining the shared transaction for {RequestName}", requestName);
        else
            _logger.LogInformation("[Transactional] Beginning the shared transaction for {RequestName}", requestName);

        _nesting.MarkTransactionOpen();

        return ambientTransaction.ExecuteAsync(_ => next(), cancellationToken);
    }

    // Reflection-based EF Core integration. The defensive null paths still local to this method
    // (Database property missing, BeginTransactionAsync method missing) cannot be exercised with a real
    // DbContext; tests cover the no-DbContext-registered and success paths via HandleAsync. The
    // EF-Core-not-loaded guard now lives in the resolver.
    [ExcludeFromCodeCoverage]
    private async Task<object?> BeginDbTransactionOrNullAsync(string requestName, CancellationToken cancellationToken = default)
    {
        var dbContext = ResolveDbContextResolver().Resolve(_serviceProvider);
        if (dbContext is null)
        {
            _logger.LogWarning(
                "[Transactional] No DbContext available; executing {RequestName} without transaction",
                requestName);
            return null;
        }

        var database = dbContext.GetType().GetProperty("Database")?.GetValue(dbContext);
        var beginMethod = database?.GetType().GetMethod("BeginTransactionAsync", [typeof(CancellationToken)]);
        if (beginMethod is null)
        {
            _logger.LogWarning(
                "[Transactional] DbContext does not expose BeginTransactionAsync; executing {RequestName} without transaction",
                requestName);
            return null;
        }

        _logger.LogInformation("[Transactional] Beginning transaction for {RequestName}", requestName);
        var task = (Task)beginMethod.Invoke(database, [cancellationToken])!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    // The resolver is a container singleton so its resolved-type cache lives for the application; a
    // behavior constructed directly against a provider that never registered it — the unit tests —
    // falls back to a fresh instance, correct but without that shared cache.
    private ITransactionDbContextResolver ResolveDbContextResolver() =>
        _serviceProvider.GetService<ITransactionDbContextResolver>() ?? new TransactionDbContextResolver();

    private async Task<TResponse> ExecuteInTransactionAsync(
        object transaction, Func<Task<TResponse>> next, string requestName, CancellationToken cancellationToken = default)
    {
        _nesting.MarkTransactionOpen();

        try
        {
            var response = await next().ConfigureAwait(false);
            await InvokeTransactionMethodAsync(transaction, "CommitAsync", cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("[Transactional] Committed transaction for {RequestName}", requestName);
            return response;
        }
        catch
        {
            // Undoing the partial write is exactly what a cancelled request needs too, so the
            // rollback deliberately does not observe the caller's token: EF Core's RollbackAsync
            // short-circuits on an already-cancelled one, which would replace the handler's real
            // exception with an OperationCanceledException and leave the undo to dispose.
            await InvokeTransactionMethodAsync(transaction, "RollbackAsync", CancellationToken.None).ConfigureAwait(false);
            _logger.LogWarning("[Transactional] Rolled back transaction for {RequestName}", requestName);
            throw;
        }
        finally
        {
            await DisposeTransactionAsync(transaction).ConfigureAwait(false);
        }
    }

    // Reflection helper: real EF Core IDbContextTransaction always exposes CommitAsync/RollbackAsync,
    // so the method-not-found branch is defensive only.
    [ExcludeFromCodeCoverage]
    private static async Task InvokeTransactionMethodAsync(object transaction, string methodName, CancellationToken cancellationToken = default)
    {
        var method = transaction.GetType().GetMethod(methodName, [typeof(CancellationToken)]);
        if (method is null)
            return;
        await ((Task)method.Invoke(transaction, [cancellationToken])!).ConfigureAwait(false);
    }

    // Real EF Core IDbContextTransaction always implements IAsyncDisposable; the IDisposable fallback
    // is defensive only and cannot be exercised in tests.
    [ExcludeFromCodeCoverage]
    private static async ValueTask DisposeTransactionAsync(object transaction)
    {
        if (transaction is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (transaction is IDisposable disposable)
            disposable.Dispose();
    }
}
