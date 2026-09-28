namespace Bsft.Mediator;

/// <summary>
/// Mediator interface for dispatching commands, queries, and notifications to their respective handlers.
/// </summary>
public interface IMediator
{
    /// <summary>
    /// Sends a command that produces a result of type <typeparamref name="TResult"/> to its handler.
    /// </summary>
    /// <typeparam name="TResult">The type of the result produced by the command.</typeparam>
    /// <param name="command">The command to send.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The result of the command execution.</returns>
    Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a command that does not produce a result to its handler.
    /// </summary>
    /// <param name="command">The command to send.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task SendAsync(ICommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a query to its handler and returns the result.
    /// </summary>
    /// <typeparam name="TResult">The type of the result returned by the query.</typeparam>
    /// <param name="query">The query to send.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The result of the query execution.</returns>
    Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a notification to all registered handlers sequentially.
    /// </summary>
    /// <typeparam name="TNotification">The type of the notification.</typeparam>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
