using Bsft.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Mediator;

/// <summary>
/// Default implementation of <see cref="IMediator"/> that resolves handlers from the service provider
/// and wraps command/query execution in pipeline behaviors.
/// </summary>
public sealed class DefaultMediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultMediator"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve handlers and behaviors.</param>
    public DefaultMediator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <inheritdoc />
    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var commandType = command.GetType();
        var handlerType = HandlerInvokers.Close(typeof(ICommandHandler<,>), commandType, typeof(TResult));

        var invoke = ResolveInvoker<TResult>(handlerType, command, cancellationToken)
            ?? throw MissingHandler("command", commandType);

        return ExecuteWithPipelineAsync(command, invoke, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var commandType = command.GetType();

        // A command without a result may be handled either by ICommandHandler<TCommand> or by
        // ICommandHandler<TCommand, Unit>; the pipeline sees the Unit shape in both cases.
        var invoke = ResolveVoidInvoker(commandType, command, cancellationToken)
            ?? ResolveInvoker<Unit>(HandlerInvokers.Close(typeof(ICommandHandler<,>), commandType, typeof(Unit)), command, cancellationToken)
            ?? throw MissingHandler("command", commandType);

        await ExecuteWithPipelineAsync<ICommand<Unit>, Unit>(command, invoke, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var queryType = query.GetType();
        var handlerType = HandlerInvokers.Close(typeof(IQueryHandler<,>), queryType, typeof(TResult));

        var invoke = ResolveInvoker<TResult>(handlerType, query, cancellationToken)
            ?? throw MissingHandler("query", queryType);

        return ExecuteWithPipelineAsync(query, invoke, cancellationToken);
    }

    /// <summary>Reports that nothing is registered to handle a request.</summary>
    /// <param name="kind">The word naming the request's kind in the message.</param>
    /// <param name="requestType">The request's runtime type.</param>
    /// <returns>The exception to throw.</returns>
    private static InvalidOperationException MissingHandler(string kind, Type requestType)
        => new($"No handler registered for {kind} type {requestType.Name}.");

    /// <summary>Resolves the handler for a request and returns the call that runs it.</summary>
    /// <typeparam name="TResult">The result the handler returns.</typeparam>
    /// <param name="handlerType">The closed handler interface to resolve.</param>
    /// <param name="request">The request to hand the handler.</param>
    /// <param name="cancellationToken">The cancellation token to hand the handler.</param>
    /// <returns>The call, or <see langword="null"/> when nothing is registered for that interface.</returns>
    private Func<Task<TResult>>? ResolveInvoker<TResult>(Type handlerType, object request, CancellationToken cancellationToken)
    {
        var handler = _serviceProvider.GetService(handlerType);

        if (handler is null)
            return null;

        var invoke = HandlerInvokers.Of<Task<TResult>>(handlerType);

        return () => invoke(handler, request, cancellationToken);
    }

    /// <summary>Resolves a handler that returns no result and reports its completion as a unit.</summary>
    /// <param name="commandType">The command's runtime type.</param>
    /// <param name="command">The command to hand the handler.</param>
    /// <param name="cancellationToken">The cancellation token to hand the handler.</param>
    /// <returns>The call, or <see langword="null"/> when no such handler is registered.</returns>
    private Func<Task<Unit>>? ResolveVoidInvoker(Type commandType, object command, CancellationToken cancellationToken)
    {
        var handlerType = HandlerInvokers.Close(typeof(ICommandHandler<>), commandType);
        var handler = _serviceProvider.GetService(handlerType);

        if (handler is null)
            return null;

        var invoke = HandlerInvokers.Of<Task>(handlerType);

        return async () =>
        {
            await invoke(handler, command, cancellationToken).ConfigureAwait(false);

            return Unit.Value;
        };
    }

    /// <inheritdoc />
    public async Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

        var handlers = _serviceProvider.GetServices<INotificationHandler<TNotification>>().ToList();
        var configuration = _serviceProvider.GetService<MediatorConfiguration>();

        using var scope = BeginPublishScope<TNotification>(configuration, handlers.Count);

        var failures = await PublishToAllAsync(handlers, notification, configuration, cancellationToken).ConfigureAwait(false);

        if (failures is null)
        {
            scope?.Complete();

            return;
        }

        var aggregate = new AggregateException(
            "One or more notification handlers, or the notification forwarder, threw an exception.",
            failures);

        scope?.Fail(aggregate);

        throw aggregate;
    }

    /// <summary>Opens the telemetry scope a publish is recorded under.</summary>
    /// <typeparam name="TNotification">The notification's type.</typeparam>
    /// <param name="configuration">The mediator's configuration, when the host registered one.</param>
    /// <param name="handlerCount">The number of handlers the notification is dispatched to.</param>
    /// <returns>The scope, or <see langword="null"/> when tracing is off.</returns>
    /// <remarks>
    /// Notifications bypass the behavior pipeline, so they are instrumented directly here. Telemetry
    /// is resolved lazily so the mediator works with minimal wiring; the scope is a no-op without an
    /// attached listener or exporter.
    /// </remarks>
    private IRequestTelemetryScope? BeginPublishScope<TNotification>(
        MediatorConfiguration? configuration, int handlerCount)
    {
        if (configuration?.ShouldTrace != true)
            return null;

        return _serviceProvider.GetService<IMediatorTelemetry>()?.BeginPublish(typeof(TNotification).Name, handlerCount);
    }

    /// <summary>Hands the notification to every local handler and, when configured, the forwarder.</summary>
    /// <typeparam name="TNotification">The notification's type.</typeparam>
    /// <param name="handlers">The registered local handlers.</param>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="configuration">The mediator's configuration, when the host registered one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The failures, or <see langword="null"/> when everything succeeded.</returns>
    /// <remarks>
    /// Forwarding runs after the local handlers, so the handlers registered with the mediator itself
    /// always observe the notification first and an external subscriber never overtakes them. A
    /// forwarding failure joins the handler failures instead of escaping on its own, so the
    /// documented AggregateException contract of PublishAsync still holds.
    /// </remarks>
    private async Task<List<Exception>?> PublishToAllAsync<TNotification>(
        List<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        MediatorConfiguration? configuration,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        var failures = await InvokeHandlersAsync(handlers, notification, cancellationToken).ConfigureAwait(false);

        if (configuration?.ShouldForwardNotifications != true)
            return failures;

        var forwardingFailure = await InvokeForwarderAsync(notification, cancellationToken).ConfigureAwait(false);

        if (forwardingFailure is not null)
            (failures ??= []).Add(forwardingFailure);

        return failures;
    }

    private static async Task<List<Exception>?> InvokeHandlersAsync<TNotification>(
        List<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        List<Exception>? failures = null;

        foreach (var handler in handlers)
        {
            try
            {
                await handler.HandleAsync(notification, cancellationToken).ConfigureAwait(false);
            }
            // Notification-handler isolation requires catching every exception type: a handler is
            // arbitrary user code, and one failure must not stop the rest. Nothing is swallowed —
            // all failures are returned to the caller and rethrown together as an AggregateException.
            // The generic-exception-catch finding on this catch clause is accepted, not suppressed.
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        return failures;
    }

    /// <summary>
    /// Hands the notification to the configured <see cref="INotificationForwarder"/> and returns the
    /// failure instead of throwing, so the caller can compose it with the handler failures.
    /// </summary>
    private async Task<Exception?> InvokeForwarderAsync<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        try
        {
            // Resolution is guarded too, not just the call: a forwarder registered without the
            // dependency it needs — the bridge added without the bus behind it — fails on
            // activation, and that failure belongs in the AggregateException like any other
            // forwarding failure rather than escaping PublishAsync raw and skipping scope.Fail.
            var forwarder = _serviceProvider.GetService<INotificationForwarder>();

            if (forwarder is null)
                return new InvalidOperationException(MediatorConfiguration.MissingForwarderMessage);

            await forwarder.ForwardAsync(notification, cancellationToken).ConfigureAwait(false);
            return null;
        }
        // The forwarding target is arbitrary user code behind a bus, exactly like a notification
        // handler, so the same isolation applies: the failure is returned and rethrown as part of
        // the AggregateException rather than swallowed. The generic-exception-catch finding on this
        // catch clause is accepted, not suppressed.
        catch (Exception ex)
        {
            return ex;
        }
    }

    private Task<TResponse> ExecuteWithPipelineAsync<TRequest, TResponse>(
        TRequest request,
        Func<Task<TResponse>> handler,
        CancellationToken cancellationToken = default)
    {
        var resolved = _serviceProvider
            .GetServices<IPipelineBehavior<TRequest, TResponse>>()
            .ToList();

        // The canonical order is applied here, not at registration time: a behavior package
        // registers its own behavior from its own AddBsft* call, which a host may make after
        // AddBsftMediator, so registration order is not something the defaults can control. The
        // orderer exists only when the host asked for the defaults; without it registration order
        // stands, which is the escape hatch for a host wiring its own chain.
        var orderer = _serviceProvider.GetService<IPipelineBehaviorOrderer>();
        var behaviors = orderer is null ? resolved : orderer.Order(resolved);

        var next = handler;

        // Folded from the inside out, so the first behavior of the ordered list is entered first.
        for (var position = behaviors.Count - 1; position >= 0; position--)
        {
            var behavior = behaviors[position];
            var currentNext = next;
            next = () => behavior.HandleAsync(request, currentNext, cancellationToken);
        }

        return next();
    }
}
