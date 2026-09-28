using Bsft.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Testing.Mediation;

/// <summary>
/// The default <see cref="IMediatorTestHarness"/>: it opens one service scope over the container it
/// was handed and resolves the mediator from it, so scoped collaborators behave as they would inside
/// a single request.
/// </summary>
public sealed class MediatorTestHarness : IMediatorTestHarness
{
    private readonly IServiceProvider _root;
    private readonly IServiceScope _scope;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediatorTestHarness"/> class, taking ownership of
    /// the container: disposing the harness disposes it.
    /// </summary>
    /// <param name="services">A container with the mediator and its handlers registered.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public MediatorTestHarness(IServiceProvider services)
    {
        _root = services ?? throw new ArgumentNullException(nameof(services));
        _scope = services.CreateScope();
        Mediator = _scope.ServiceProvider.GetRequiredService<IMediator>();
    }

    /// <inheritdoc />
    public IMediator Mediator { get; }

    /// <inheritdoc />
    public IServiceProvider Services => _scope.ServiceProvider;

    /// <inheritdoc />
    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default) =>
        Mediator.SendAsync(command, cancellationToken);

    /// <inheritdoc />
    public Task SendAsync(ICommand command, CancellationToken cancellationToken = default) =>
        Mediator.SendAsync(command, cancellationToken);

    /// <inheritdoc />
    public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default) =>
        Mediator.QueryAsync(query, cancellationToken);

    /// <inheritdoc />
    public Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification =>
        Mediator.PublishAsync(notification, cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        _scope.Dispose();
        (_root as IDisposable)?.Dispose();
    }
}
