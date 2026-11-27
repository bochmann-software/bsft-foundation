using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Bsft.Data.Transactions;

/// <summary>
/// Hands the open shared transaction to an enlisted context the moment EF Core initializes it, and
/// tells the scope when such a context was disposed.
/// </summary>
/// <remarks>
/// <para>
/// EF Core has no interceptor for a context's creation, and the command interceptor runs only at the
/// first command. Code that asks <c>Database.CurrentTransaction</c> before that, to join a caller's
/// transaction or begin one of its own (the hierarchy, organization and billing stores do), would find
/// none and begin a second transaction on the shared connection. EF Core raises
/// <see cref="CoreEventId.ContextInitialized"/> synchronously while that first call initializes the
/// context, once its services are usable, so enlisting there hands the context the transaction before
/// the call returns.
/// </para>
/// <para>
/// The events arrive through EF Core's process-wide diagnostic listener. Each context is matched to its
/// scope through the <see cref="SharedTransactionInterceptor"/> in its own options, so a context of
/// another scope or another container is never handed this scope's transaction, and a context without
/// the interceptor is left alone. <see cref="CoreEventId.ContextDisposed"/> is raised after the context
/// is disposed, when its options are no longer readable, so the interceptor is remembered per
/// initialized context for as long as the context lives.
/// </para>
/// <para>
/// One instance per container, subscribed until the container is disposed.
/// </para>
/// </remarks>
internal sealed class SharedContextLifetimeListener
    : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private readonly ConditionalWeakTable<DbContext, SharedTransactionInterceptor> _interceptors = new();
    private readonly List<IDisposable> _subscriptions = [];
    private readonly object _sync = new();
    private bool _disposed;

    /// <summary>Starts listening to EF Core's diagnostic listener.</summary>
    /// <returns>This listener, subscribed.</returns>
    public SharedContextLifetimeListener Subscribe()
    {
        Keep(DiagnosticListener.AllListeners.Subscribe(this));
        return this;
    }

    /// <inheritdoc />
    public void OnNext(DiagnosticListener value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (string.Equals(value.Name, DbLoggerCategory.Name, StringComparison.Ordinal))
            Keep(value.Subscribe(this, static (name, _, _) => IsLifetimeEvent(name)));
    }

    /// <inheritdoc />
    public void OnNext(KeyValuePair<string, object?> value)
    {
        switch (value.Value)
        {
            case ContextInitializedEventData { Context: { } context, ContextOptions: var options }:
                OnInitialized(context, options);
                break;
            case DbContextEventData { Context: { } context } when IsDisposedEvent(value.Key):
                OnDisposed(context);
                break;
        }
    }

    /// <inheritdoc />
    public void OnCompleted()
    {
        // A listener completes when it is disposed; there is nothing of this observer's to release.
    }

    /// <inheritdoc />
    public void OnError(Exception error)
    {
        // DiagnosticListener never reports errors to its observers.
    }

    /// <inheritdoc />
    public void Dispose()
    {
        IDisposable[] subscriptions;

        lock (_sync)
        {
            _disposed = true;
            subscriptions = [.. _subscriptions];
            _subscriptions.Clear();
        }

        foreach (var subscription in subscriptions)
            subscription.Dispose();
    }

    /// <summary>Whether an EF Core event is one this listener handles.</summary>
    /// <param name="name">The event name.</param>
    /// <returns><see langword="true"/> for the initialization and the disposal of a context.</returns>
    internal static bool IsLifetimeEvent(string name) =>
        string.Equals(name, CoreEventId.ContextInitialized.Name, StringComparison.Ordinal) || IsDisposedEvent(name);

    private static bool IsDisposedEvent(string name) =>
        string.Equals(name, CoreEventId.ContextDisposed.Name, StringComparison.Ordinal);

    private void OnInitialized(DbContext context, DbContextOptions options)
    {
        var interceptor = options
            .FindExtension<CoreOptionsExtension>()?
            .Interceptors?
            .OfType<SharedTransactionInterceptor>()
            .FirstOrDefault();

        if (interceptor is null)
            return;

        _interceptors.AddOrUpdate(context, interceptor);
        interceptor.EnlistInitialized(context);
    }

    private void OnDisposed(DbContext context)
    {
        if (_interceptors.TryGetValue(context, out var interceptor))
            interceptor.ForgetDisposed(context);
    }

    /// <summary>
    /// Keeps a subscription for disposal, or disposes it at once when the listener is already disposed,
    /// so that a diagnostic listener created while the container shuts down does not outlive it.
    /// </summary>
    private void Keep(IDisposable subscription)
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                _subscriptions.Add(subscription);
                return;
            }
        }

        subscription.Dispose();
    }
}
