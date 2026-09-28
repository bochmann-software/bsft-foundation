using System.Collections.Concurrent;
using System.Reflection;
using Bsft.Messaging.Publishing;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Messaging.Consuming;

/// <summary>
/// The default <see cref="IIntegrationEventHandlerInvoker"/>: it resolves the handlers registered for
/// the event's runtime type from the caller's scope and runs them all, one at a time.
/// </summary>
internal sealed class IntegrationEventHandlerInvoker : IIntegrationEventHandlerInvoker
{
    private static readonly MethodInfo _invokeTypedMethod = typeof(IntegrationEventHandlerInvoker)
        .GetMethod(nameof(InvokeTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    // The event type is only known once the message has been decoded, but handlers are resolved by a
    // generic service type. Closing the generic method once per type and keeping the resulting delegate
    // turns every later delivery into a direct call instead of a reflective invoke with its boxed
    // arguments — and the same handful of event types repeats for the lifetime of the process.
    private readonly ConcurrentDictionary<Type, Func<IServiceProvider, IIntegrationEvent, CancellationToken, Task<int>>> _invokers = new();

    /// <inheritdoc />
    public Task<int> InvokeAllAsync(
        IServiceProvider handlerServices, IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (handlerServices is null)
            throw new ArgumentNullException(nameof(handlerServices));
        if (integrationEvent is null)
            throw new ArgumentNullException(nameof(integrationEvent));

        var invoke = _invokers.GetOrAdd(integrationEvent.GetType(), static type => InvokerFor(type));

        return invoke(handlerServices, integrationEvent, cancellationToken);
    }

    private static Func<IServiceProvider, IIntegrationEvent, CancellationToken, Task<int>> InvokerFor(Type eventType) =>
        _invokeTypedMethod
            .MakeGenericMethod(eventType)
            .CreateDelegate<Func<IServiceProvider, IIntegrationEvent, CancellationToken, Task<int>>>();

    /// <summary>
    /// Runs every handler registered for <typeparamref name="TEvent"/> and reports how many there were.
    /// Every handler runs, even when an earlier one fails, so one broken subscriber cannot stop the
    /// others from seeing the event; the failures are reported together afterwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handlers run one after another, in registration order, and that is a contract rather than an
    /// implementation detail. They share the caller's scope, and with the EF Core inbox that scope's
    /// <c>DbContext</c> is exactly what they are documented to write through — the store stages the
    /// inbox mark on it so that the mark and their work commit together. A <c>DbContext</c> permits one
    /// operation at a time, so running two handlers at once would fail every attempt of a delivery that
    /// has two of them and park an event nobody ever handled. Giving each handler its own scope would
    /// avoid the clash and destroy the thing it protects: the mark would no longer be staged on the
    /// context the handlers write through, and effective-once would decay into two separate commits.
    /// Sequential invocation keeps the composition intact, and matches a consumer that takes one
    /// message at a time anyway — the concurrency worth having is between messages, not inside one.
    /// </para>
    /// <para>
    /// A withdrawal supersedes a failure already recorded, which is the one precedence running in
    /// sequence forces a choice about. The consumer counts a failure as a delivery attempt and spends
    /// one of the attempts that end in parking, but treats a shutdown as no attempt at all; reporting
    /// the failure would therefore make every restart that coincides with a broken handler push that
    /// message closer to being parked. Nothing is lost by staying quiet: the delivery is abandoned
    /// either way, and the redelivery meets the same handler and the same failure.
    /// </para>
    /// </remarks>
    private static async Task<int> InvokeTypedAsync<TEvent>(
        IServiceProvider services, IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        var handlers = services.GetServices<IIntegrationEventHandler<TEvent>>().ToList();

        if (handlers.Count == 0)
            return 0;

        var typedEvent = (TEvent)integrationEvent;
        List<Exception>? failures = null;

        foreach (var handler in handlers)
        {
            // Sequential invocation is what makes this question askable at all: a delivery the caller
            // withdrew mid-run is going to be abandoned and redelivered whatever the remaining handlers
            // do, so starting another one is work whose result is thrown away.
            cancellationToken.ThrowIfCancellationRequested();

            var delivery = InvokeAsync(handler, typedEvent, cancellationToken);

            // Awaiting WhenAny observes the outcome without unwinding, which is what lets the fault be
            // translated into a MessagePublishException the caller can act on rather than a raw handler
            // exception it would have to guess about.
            await Task.WhenAny(delivery).ConfigureAwait(false);

            if (delivery.IsFaulted)
            {
                (failures ??= []).AddRange(delivery.Exception!.Flatten().InnerExceptions);
                continue;
            }

            // Completes immediately for a successful handler and surfaces cancellation as itself. Only
            // the caller's own token can get here — a handler cancelled by any other token has already
            // been turned into a fault above — so a withdrawn delivery stops here rather than running
            // the remaining handlers against a token that is already gone.
            await delivery.ConfigureAwait(false);
        }

        if (failures is not null)
        {
            throw new MessagePublishException(
                $"Publishing integration event '{typeof(TEvent).FullName}' failed in {failures.Count} of {handlers.Count} handlers.",
                new AggregateException(failures));
        }

        return handlers.Count;
    }

    /// <summary>
    /// Invokes a handler through an async method, so a handler that throws before its first await
    /// still fails through its task instead of tearing down the whole delivery.
    /// </summary>
    /// <remarks>
    /// A handler that ends in cancellation without the delivery having been cancelled was stopped by a
    /// token of its own — an HTTP client timeout is the usual one — and that is a failed delivery like
    /// any other. It has to be turned into a failure here: an async method that
    /// throws an <see cref="OperationCanceledException"/> completes its task as cancelled rather than
    /// faulted, so the aggregated delivery task would end up cancelled too, and the caller would
    /// report a shutdown that nobody asked for instead of the failure it has to retry.
    /// </remarks>
    private static async Task InvokeAsync<TEvent>(
        IIntegrationEventHandler<TEvent> handler, TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        try
        {
            await handler.HandleAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException cancellation) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MessagePublishException(
                $"The handler '{handler.GetType().FullName}' was canceled by a token of its own while the delivery was still wanted.",
                cancellation);
        }
    }
}
