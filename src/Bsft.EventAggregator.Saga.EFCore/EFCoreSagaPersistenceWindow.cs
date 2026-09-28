using Bsft.Core;
using Bsft.EventAggregator.Saga.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.EventAggregator.Saga.EFCore;

/// <summary>
/// The EF Core persistence window. It resolves one <see cref="ISagaTimeoutDbContext"/> for the
/// window, publishes it to the saga stores through <see cref="ISagaDbContextAccessor"/>, and commits
/// the state advance together with the step's timeout writes in a single <c>SaveChangesAsync</c>.
/// </summary>
/// <remarks>
/// Window and accessor are the same type on purpose: whoever opens the window is the only component
/// that can know which context is current, and splitting the two would force the stores to depend on
/// a concrete class in order to reach the setter.
/// <para>
/// The context travels through an <see cref="AsyncLocal{T}"/> rather than through the store
/// signatures, because the stores are singletons behind provider-agnostic interfaces that must not
/// grow an EF Core parameter. The same mechanism already carries the correlation id of a firing
/// timeout. <see cref="OpenAsync"/> is deliberately not an <c>async</c> method: the ambient value has
/// to be written on the caller's execution context to be visible to the stores it then calls.
/// </para>
/// </remarks>
public sealed class EFCoreSagaPersistenceWindow : ISagaPersistenceWindow, ISagaDbContextAccessor
{
    // One ambient snapshot per open window: the shared context together with the post-commit stamps
    // registered by the stores against it. A store defers stamping its instance's concurrency token
    // into that list so a refused commit leaves the instance untouched. The two used to be two
    // AsyncLocal fields written and restored in lockstep, a desync trap; folding them into a single
    // ambient value makes the pair impossible to split. Ambient because the stores reach it through
    // the accessor without a window handle of their own.
    private readonly AsyncLocal<WindowState?> _state = new();
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="EFCoreSagaPersistenceWindow"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory the window's DbContext is resolved from.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scopeFactory"/> is null.</exception>
    public EFCoreSagaPersistenceWindow(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    /// <inheritdoc />
    public ISagaTimeoutDbContext? Current => _state.Value?.Db;

    /// <inheritdoc />
    public void OnCommitted(Action onCommitted)
    {
        if (onCommitted is null) throw new ArgumentNullException(nameof(onCommitted));

        // Only meaningful while a window is open; the state-only store never opens one and stamps its
        // instance inline on its own commit instead. No open window means no snapshot to defer into, so
        // the callback is silently dropped — mirroring the null Current.
        _state.Value?.Pending.Add(onCommitted);
    }

    /// <inheritdoc />
    public Task<ISagaPersistenceWindowScope> OpenAsync(CancellationToken cancellationToken = default)
    {
        var scope = _scopeFactory.CreateAsyncScope();

        ISagaTimeoutDbContext db;
        try
        {
            db = scope.ServiceProvider.GetRequiredService<ISagaTimeoutDbContext>();
        }
        catch (InvalidOperationException)
        {
            // The scope owns a context that was never handed out. Leaking it would leak a pooled
            // connection on every step of a misconfigured application, so it goes before the throw.
            scope.Dispose();
            throw;
        }

        var state = new WindowState(db, new List<Action>());
        var window = new Window(scope, _state, _state.Value, state);
        _state.Value = state;

        return Task.FromResult<ISagaPersistenceWindowScope>(window);
    }

    /// <summary>
    /// The ambient snapshot of one open window: its shared context and the post-commit stamps deferred
    /// against it. Carried as a single value so the context and its pending stamps can never desync.
    /// </summary>
    /// <param name="Db">The context every store on this flow stages into.</param>
    /// <param name="Pending">The concurrency-token stamps to run once the window's commit is durable.</param>
    private sealed record WindowState(ISagaTimeoutDbContext Db, List<Action> Pending);

    /// <summary>One open window: its scope, and the single ambient snapshot it carries and restores.</summary>
    private sealed class Window : ISagaPersistenceWindowScope
    {
        private readonly AsyncServiceScope _scope;
        private readonly AsyncLocal<WindowState?> _ambient;
        private readonly WindowState? _previous;
        private readonly WindowState _state;

        public Window(
            AsyncServiceScope scope,
            AsyncLocal<WindowState?> ambient,
            WindowState? previous,
            WindowState state)
        {
            _scope = scope;
            _ambient = ambient;
            _previous = previous;
            _state = state;
        }

        /// <inheritdoc />
        /// <remarks>
        /// The staged step save carries the saga row's optimistic-concurrency token, so this is where a
        /// writer that advanced the row between the window's read and this commit is caught. The raw
        /// provider concurrency failure is translated by <see cref="SagaConcurrencyGuard"/> into the same
        /// <see cref="PreconditionFailedException"/> the store's self-committing path raises, so the
        /// windowed path surfaces the conflict identically instead of leaking a provider exception. The
        /// state advance and the step's timeout writes share this one <c>SaveChangesAsync</c>, so a
        /// refusal commits neither — no timeout is scheduled for a saga whose save was refused. Only once
        /// the commit is durable are the deferred concurrency tokens stamped onto their caller instances.
        /// </remarks>
        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await SagaConcurrencyGuard
                .SaveChangesOrRefuseAsync(_state.Db, Conflict, cancellationToken)
                .ConfigureAwait(false);

            // Durable now: stamp every deferred concurrency token onto its caller's instance. A refusal
            // above threw before reaching here, so a refused commit leaves those instances untouched.
            foreach (var stamp in _state.Pending)
                stamp();
        }

        /// <summary>Builds the generic refusal the windowed commit surfaces on a concurrency conflict.</summary>
        private static PreconditionFailedException Conflict() =>
            new("A saga row was changed by another writer while its step was being committed. Reload it and re-handle the event.");

        /// <remarks>
        /// Not an <c>async</c> method, for the same reason <see cref="OpenAsync"/> is not: the state
        /// machine of an async method restores the execution context it started with, so an ambient
        /// value written inside one never reaches the caller — and the caller would go on seeing a
        /// context that has just been disposed.
        /// </remarks>
        public ValueTask DisposeAsync()
        {
            // Restoring rather than clearing keeps an enclosing window intact, and is identical to
            // clearing in the ordinary case where there is none. One value carries both the context and
            // its pending stamps, so the pair is restored together and can never desync.
            _ambient.Value = _previous;
            return _scope.DisposeAsync();
        }
    }
}
