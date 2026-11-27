using Bsft.Core.Persistence;
using Bsft.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using System.Collections.Concurrent;
using System.Reflection;

namespace Bsft.Resilience.Mediator;

/// <summary>
/// Mediator pipeline behavior that retries requests marked with <see cref="Bsft.Mediator.RetryableAttribute"/> (or the
/// older <see cref="RetryableAttribute"/>) on
/// transient failures, using the shared resilience pipeline. Requests without the marker pass
/// through untouched, so the behavior adds no overhead to non-retryable requests.
/// </summary>
/// <remarks>
/// <para>
/// Only the outermost request of a scope is retried. A request dispatched while an outer request of
/// the same scope is retried (<see cref="IRequestNesting"/>) passes through: retrying it alone inside
/// the outer attempt would multiply the attempts. The failure reaches the outermost attempt instead,
/// which is what runs again.
/// </para>
/// <para>
/// A request dispatched while an outer request holds a transaction open passes through as well, but
/// only in a host whose nested requests join that transaction: one that registered an
/// <see cref="IAmbientTransaction"/>, or turned on
/// <see cref="IMediatorTransactionPolicy.ShouldApplyTransactionToAllCommands"/>. Inside a joined
/// transaction the store has typically aborted with the failure, so a retry of the nested request
/// alone cannot succeed. Any other host keeps retrying a nested request marked
/// <see cref="RetryableAttribute"/> on its own, as before.
/// </para>
/// <para>
/// When the scope has an <see cref="IAmbientTransaction"/> — the shared transaction across several
/// contexts of <c>Bsft.Data</c> — the outermost attempt is also retried on every failure the store's
/// provider classifies as a transaction conflict, and every attempt after the first runs in a fresh
/// dependency-injection scope: a new connection, new contexts, a new transaction, nothing the failed
/// attempt left behind. The host carries the state of the request's scope across with an
/// <see cref="IRequestScopeSeed"/>, which runs before anything in the fresh scope resolves the ambient
/// transaction. A request already inside an open unit of the ambient transaction passes through as well.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The type of the request.</typeparam>
/// <typeparam name="TResponse">The type of the response.</typeparam>
public sealed class RetryBehavior<TRequest, TResponse> : IRescopingPipelineBehavior<TRequest, TResponse>
{
    // The pipeline dispatches with the declared request contract as TRequest, so the marker has to be
    // read from the runtime type. The result is cached per type to keep reflection off the hot path.
    private static readonly ConcurrentDictionary<Type, bool> RetryableTypes = new();

    private readonly IMediatorResiliencePipelineProvider _pipelineProvider;
    private readonly Predicate<Type> _isRetriedByPolicy;
    private readonly IRequestNesting _nesting;
    private readonly bool _isTransactionPolicyOn;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryBehavior{TRequest, TResponse}"/> class without
    /// the retry policy and the scope's nesting marks.
    /// </summary>
    /// <param name="pipelineProvider">The provider of the shared resilience pipeline.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pipelineProvider"/> is null.</exception>
    /// <remarks>
    /// Kept for compatibility with code that constructs the behavior itself: only requests marked
    /// <see cref="RetryableAttribute"/> are retried, and every request counts as the outermost of its
    /// scope (<see cref="NullRequestNesting"/>). The container picks the other constructor.
    /// </remarks>
    public RetryBehavior(IMediatorResiliencePipelineProvider pipelineProvider)
        : this(pipelineProvider, static _ => false, NullRequestNesting.Instance, isTransactionAppliedToAllCommands: false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryBehavior{TRequest, TResponse}"/> class that
    /// follows <see cref="BsftResilienceMediatorOptions.ShouldRetryAllCommands"/> and retries only the
    /// outermost request of a scope. The container picks it.
    /// </summary>
    /// <param name="pipelineProvider">The provider of the shared resilience pipeline.</param>
    /// <param name="options">The mediator resilience options carrying the retry policy.</param>
    /// <param name="metadataProvider">Says which requests are commands.</param>
    /// <param name="nesting">The scope's marks of the requests being retried or holding a transaction open.</param>
    /// <param name="transactionPolicy">
    /// The mediator's transaction policy, which says whether a nested command joins the transaction an
    /// outer one holds open.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public RetryBehavior(
        IMediatorResiliencePipelineProvider pipelineProvider,
        IOptions<BsftResilienceMediatorOptions> options,
        IRequestMetadataProvider metadataProvider,
        IRequestNesting nesting,
        IMediatorTransactionPolicy transactionPolicy)
        : this(
            pipelineProvider,
            CommandsByPolicy(options, metadataProvider),
            nesting ?? throw new ArgumentNullException(nameof(nesting)),
            (transactionPolicy ?? throw new ArgumentNullException(nameof(transactionPolicy))).ShouldApplyTransactionToAllCommands)
    {
    }

    private RetryBehavior(
        IMediatorResiliencePipelineProvider pipelineProvider,
        Predicate<Type> isRetriedByPolicy,
        IRequestNesting nesting,
        bool isTransactionAppliedToAllCommands)
    {
        _pipelineProvider = pipelineProvider ?? throw new ArgumentNullException(nameof(pipelineProvider));
        _isRetriedByPolicy = isRetriedByPolicy;
        _nesting = nesting;
        _isTransactionPolicyOn = isTransactionAppliedToAllCommands;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The delegate overload knows no scope, so every attempt reruns <paramref name="next"/> in the
    /// request's scope and only the host's predicate decides. For the same reason it cannot ask the
    /// scope for an <see cref="IAmbientTransaction"/>: it passes a request nested in an open transaction
    /// through only under the all-commands transaction policy. The mediator calls the continuation
    /// overload.
    /// </remarks>
    public async Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken = default)
    {
        if (next is null)
            throw new ArgumentNullException(nameof(next));

        if (!IsRetriedHere(request) || (_isTransactionPolicyOn && _nesting.IsTransactionOpen))
            return await next().ConfigureAwait(false);

        return await RetryInRequestScopeAsync(request!.GetType(), next, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="next"/> is null.</exception>
    public Task<TResponse> HandleAsync(TRequest request, IPipelineContinuation<TResponse> next, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (!IsRetriedHere(request))
            return next.RunAsync(static (_, rest) => rest(), cancellationToken);

        var requestType = request!.GetType();

        return next.RunAsync((requestScope, rest) => RetryAsync(requestType, requestScope, rest, next, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Says which requests the policy retries: every command when
    /// <see cref="BsftResilienceMediatorOptions.ShouldRetryAllCommands"/> is on, none otherwise.
    /// </summary>
    private static Predicate<Type> CommandsByPolicy(
        IOptions<BsftResilienceMediatorOptions> options, IRequestMetadataProvider metadataProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metadataProvider);

        return options.Value.ShouldRetryAllCommands
            ? requestType => metadataProvider.GetMetadata(requestType).IsCommand
            : static _ => false;
    }

    /// <summary>
    /// Retries the outermost request: over the ambient transaction with the conflict classification and a
    /// fresh scope per further attempt, without one in the request's scope.
    /// </summary>
    private async Task<TResponse> RetryAsync(
        Type requestType,
        IServiceProvider requestScope,
        Func<Task<TResponse>> rest,
        IPipelineContinuation<TResponse> continuation,
        CancellationToken cancellationToken)
    {
        var ambientTransaction = requestScope.GetService<IAmbientTransaction>();

        // Nested in a transaction an outer request holds open, which this request joined: the failure
        // has typically aborted it, so only the outer request's retry can succeed.
        if (_nesting.IsTransactionOpen && (_isTransactionPolicyOn || ambientTransaction is not null))
            return await rest().ConfigureAwait(false);

        if (ambientTransaction is null)
            return await RetryInRequestScopeAsync(requestType, rest, cancellationToken).ConfigureAwait(false);

        // A unit opened outside the mediator is already running: the request is nested in it, and the
        // failure belongs to whoever opened it.
        if (ambientTransaction.IsActive)
            return await rest().ConfigureAwait(false);

        _nesting.MarkRetrying();

        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(MediatorResiliencePipelineProvider.RequestTypeKey, requestType);

        try
        {
            var attempts = new AmbientAttempts(ambientTransaction, rest, continuation);

            return await _pipelineProvider.Pipeline
                .ExecuteAsync(attempts.RunAsync, context)
                .ConfigureAwait(false);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    /// <summary>Retries the rest of the pipeline in the request's scope, on the host's predicate alone.</summary>
    private async Task<TResponse> RetryInRequestScopeAsync(
        Type requestType, Func<Task<TResponse>> next, CancellationToken cancellationToken)
    {
        _nesting.MarkRetrying();

        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(MediatorResiliencePipelineProvider.RequestTypeKey, requestType);

        try
        {
            // Pass next as Polly state with a non-async static callback so a retryable request neither
            // allocates a closure capturing next nor spins up an extra async state machine per attempt;
            // the handler's Task is wrapped directly in a ValueTask.
            return await _pipelineProvider.Pipeline
                .ExecuteAsync(static (_, handler) => new ValueTask<TResponse>(handler()), context, next)
                .ConfigureAwait(false);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    /// <summary>
    /// Reports whether this behavior may retry the request: it is retryable, and no outer request of
    /// the scope is retried around it. Whether a transaction is open around it that it joined is
    /// decided per attempt, where the request's scope says whether it has an ambient transaction.
    /// </summary>
    private bool IsRetriedHere(TRequest request) =>
        request is not null
        && ShouldRetryType(request.GetType())
        && !_nesting.IsRetrying;

    private bool ShouldRetryType(Type requestType) =>
        IsRetryable(requestType) || _isRetriedByPolicy(requestType);

    private static bool IsRetryable(Type requestType) =>
        RetryableTypes.GetOrAdd(
            requestType,
            static type => type.GetCustomAttribute<Bsft.Mediator.RetryableAttribute>(inherit: true) is not null
                || type.GetCustomAttribute<RetryableAttribute>(inherit: true) is not null);

    /// <summary>
    /// The attempts of one outermost request over the ambient transaction: the first in the request's
    /// scope, every further one in a fresh scope, each classified by the transaction it ran in.
    /// </summary>
    private sealed class AmbientAttempts(
        IAmbientTransaction requestTransaction,
        Func<Task<TResponse>> rest,
        IPipelineContinuation<TResponse> continuation)
    {
        private bool _isFirstAttempt = true;

        /// <summary>Runs one attempt.</summary>
        /// <param name="context">The attempt's resilience context, which carries its transaction to the predicate.</param>
        /// <returns>The response of the attempt.</returns>
        public async ValueTask<TResponse> RunAsync(ResilienceContext context)
        {
            if (_isFirstAttempt)
            {
                _isFirstAttempt = false;
                context.Properties.Set(MediatorResiliencePipelineProvider.AmbientTransactionKey, requestTransaction);

                return await rest().ConfigureAwait(false);
            }

            // Forgotten before the fresh scope is built, so a failure while the fresh attempt is still
            // being set up is not classified by the previous attempt's transaction and its failure.
            context.Properties.Set(MediatorResiliencePipelineProvider.AmbientTransactionKey, null);

            return await continuation
                .RunInNewScopeAsync((scope, freshRest) => RunFreshAsync(context, scope, freshRest), context.CancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Runs a further attempt in its fresh scope: marked as retried there, so the commands it
        /// dispatches are not retried on their own, and classified by that scope's ambient transaction.
        /// </summary>
        private static Task<TResponse> RunFreshAsync(
            ResilienceContext context, IServiceProvider scope, Func<Task<TResponse>> freshRest)
        {
            scope.GetRequiredService<IRequestNesting>().MarkRetrying();
            context.Properties.Set(
                MediatorResiliencePipelineProvider.AmbientTransactionKey, scope.GetRequiredService<IAmbientTransaction>());

            return freshRest();
        }
    }
}
