using Bsft.Mediator;

namespace Bsft.Testing.Mediation;

/// <summary>
/// A running mediator with exactly the handlers and behaviors a test asked for, and nothing else.
/// </summary>
/// <remarks>
/// Disposing the harness disposes the container it was built on, so a test should own it for the
/// duration of a single test method.
/// </remarks>
public interface IMediatorTestHarness : IDisposable
{
    /// <summary>Gets the mediator under test.</summary>
    IMediator Mediator { get; }

    /// <summary>
    /// Gets the scoped service provider the mediator resolves from, for asserting on a collaborator
    /// or fetching a registered test double.
    /// </summary>
    IServiceProvider Services { get; }

    /// <summary>Sends a command that returns a result.</summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="command">The command to send.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The handler's result.</returns>
    Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);

    /// <summary>Sends a command that returns no result.</summary>
    /// <param name="command">The command to send.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the handler has run.</returns>
    Task SendAsync(ICommand command, CancellationToken cancellationToken = default);

    /// <summary>Executes a query.</summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="query">The query to execute.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The handler's result.</returns>
    Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);

    /// <summary>Publishes a notification to every registered handler.</summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when all handlers have run.</returns>
    Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
