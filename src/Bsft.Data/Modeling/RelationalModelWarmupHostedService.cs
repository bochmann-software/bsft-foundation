using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bsft.Data.Modeling;

/// <summary>
/// Builds the relational model of every relational context type as its first instance initializes,
/// from before the first hosted service starts until the host has stopped.
/// </summary>
/// <remarks>
/// <para>
/// It listens to EF Core's diagnostic listener rather than resolving the host's contexts itself, so it
/// needs no knowledge of how a context is registered (<c>AddDbContext</c>, a factory, a pool, or
/// <c>new</c>), constructs nothing a request scope would have to supply, and never builds a model the
/// host does not use. Each context costs one annotation lookup once its model is built.
/// </para>
/// <para>
/// The subscription starts in <see cref="StartingAsync"/>, which the host calls for every lifecycle
/// service before it starts any hosted service, so a background worker that opens contexts as it
/// starts is covered as well as the first requests. <see cref="BsftDataOptions.ShouldWarmRelationalModels"/>
/// set to <see langword="false"/> leaves EF Core's lazy build untouched.
/// </para>
/// </remarks>
internal sealed class RelationalModelWarmupHostedService : IHostedLifecycleService, IObserver<DiagnosticListener>, IDisposable
{
    private readonly IOptions<BsftDataOptions> _options;
    private readonly Action<DbContext> _warm;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly object _sync = new();
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="RelationalModelWarmupHostedService"/> class.</summary>
    /// <param name="options">The data options, read for <see cref="BsftDataOptions.ShouldWarmRelationalModels"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public RelationalModelWarmupHostedService(IOptions<BsftDataOptions> options)
        : this(options, RelationalModelWarmup.EnsureBuilt)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RelationalModelWarmupHostedService"/> class.</summary>
    /// <param name="options">The data options, read for <see cref="BsftDataOptions.ShouldWarmRelationalModels"/>.</param>
    /// <param name="warm">Builds the relational model of the context it is given.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    internal RelationalModelWarmupHostedService(IOptions<BsftDataOptions> options, Action<DbContext> warm)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _warm = warm ?? throw new ArgumentNullException(nameof(warm));
    }

    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (_options.Value.ShouldWarmRelationalModels)
            Keep(DiagnosticListener.AllListeners.Subscribe(this));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void OnNext(DiagnosticListener value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (string.Equals(value.Name, DbLoggerCategory.Name, StringComparison.Ordinal))
        {
            Keep(value.Subscribe(
                new ContextInitializedObserver(_warm),
                static (name, _, _) => ContextInitializedObserver.IsContextInitialized(name)));
        }
    }

    /// <inheritdoc />
    public void OnCompleted()
    {
        // AllListeners never completes.
    }

    /// <inheritdoc />
    public void OnError(Exception error)
    {
        // AllListeners never reports errors.
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

    /// <summary>
    /// Keeps a subscription for disposal, or disposes it at once when the service is already disposed,
    /// so that a listener created while the host stops does not outlive it.
    /// </summary>
    /// <param name="subscription">The subscription.</param>
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
