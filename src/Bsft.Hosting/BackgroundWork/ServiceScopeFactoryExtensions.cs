using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Hosting.BackgroundWork;

/// <summary>
/// Runs one operation against one scoped service, and disposes the scope afterwards whatever the
/// operation did.
/// </summary>
/// <remarks>
/// <para>
/// The three lines every background service writes before it can do anything — create a scope,
/// resolve the service, dispose the scope — are three lines that are wrong in the same two ways every
/// time: a scope disposed synchronously although something in it is only
/// <see cref="IAsyncDisposable"/>, and a scope left undisposed on the path where the operation threw.
/// A hosted service that does this on every pass leaks a scope per failure, which is the shape of a
/// slow memory leak nobody attributes to the loop that caused it.
/// </para>
/// <para>
/// The async overloads create an <see cref="AsyncServiceScope"/> and dispose it asynchronously, which
/// is what a scoped <c>DbContext</c> and a pooled connection want. The synchronous overloads exist for
/// the callers that have nothing to await — reading a resolved options snapshot, say — and are not a
/// shortcut for the asynchronous ones.
/// </para>
/// </remarks>
public static class ServiceScopeFactoryExtensions
{
    /// <summary>
    /// Resolves <typeparamref name="TService"/> from a fresh scope and runs an asynchronous operation
    /// against it.
    /// </summary>
    /// <typeparam name="TService">The scoped service to resolve.</typeparam>
    /// <param name="scopeFactory">The factory the scope is created from.</param>
    /// <param name="operation">What to do with the resolved service.</param>
    /// <param name="cancellationToken">A cancellation token handed to the operation.</param>
    /// <returns>A task that completes when the operation has finished and the scope is disposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the service is not registered.</exception>
    public static async Task RunScopedAsync<TService>(
        this IServiceScopeFactory scopeFactory,
        Func<TService, CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
        where TService : notnull
    {
        if (scopeFactory is null)
            throw new ArgumentNullException(nameof(scopeFactory));
        if (operation is null)
            throw new ArgumentNullException(nameof(operation));

        var scope = scopeFactory.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            await operation(scope.ServiceProvider.GetRequiredService<TService>(), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves <typeparamref name="TService"/> from a fresh scope, runs an asynchronous operation
    /// against it and returns what the operation produced.
    /// </summary>
    /// <typeparam name="TService">The scoped service to resolve.</typeparam>
    /// <typeparam name="TResult">What the operation produces.</typeparam>
    /// <param name="scopeFactory">The factory the scope is created from.</param>
    /// <param name="operation">What to do with the resolved service.</param>
    /// <param name="cancellationToken">A cancellation token handed to the operation.</param>
    /// <returns>The result of the operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the service is not registered.</exception>
    /// <remarks>
    /// The result outlives the scope, so it must not be — or hold — something the scope owns. An entity
    /// tracked by a scoped <c>DbContext</c> returned from here is a detached graph whose lazy loading
    /// throws at the caller, which is why the callers that read data return a projection.
    /// </remarks>
    public static async Task<TResult> RunScopedAsync<TService, TResult>(
        this IServiceScopeFactory scopeFactory,
        Func<TService, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
        where TService : notnull
    {
        if (scopeFactory is null)
            throw new ArgumentNullException(nameof(scopeFactory));
        if (operation is null)
            throw new ArgumentNullException(nameof(operation));

        var scope = scopeFactory.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            return await operation(scope.ServiceProvider.GetRequiredService<TService>(), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves <typeparamref name="TService"/> from a fresh scope and runs a synchronous operation
    /// against it.
    /// </summary>
    /// <typeparam name="TService">The scoped service to resolve.</typeparam>
    /// <param name="scopeFactory">The factory the scope is created from.</param>
    /// <param name="operation">What to do with the resolved service.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the service is not registered.</exception>
    public static void RunScoped<TService>(this IServiceScopeFactory scopeFactory, Action<TService> operation)
        where TService : notnull
    {
        if (scopeFactory is null)
            throw new ArgumentNullException(nameof(scopeFactory));
        if (operation is null)
            throw new ArgumentNullException(nameof(operation));

        using var scope = scopeFactory.CreateScope();

        operation(scope.ServiceProvider.GetRequiredService<TService>());
    }

    /// <summary>
    /// Resolves <typeparamref name="TService"/> from a fresh scope, runs a synchronous operation
    /// against it and returns what the operation produced.
    /// </summary>
    /// <typeparam name="TService">The scoped service to resolve.</typeparam>
    /// <typeparam name="TResult">What the operation produces.</typeparam>
    /// <param name="scopeFactory">The factory the scope is created from.</param>
    /// <param name="operation">What to do with the resolved service.</param>
    /// <returns>The result of the operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the service is not registered.</exception>
    public static TResult RunScoped<TService, TResult>(
        this IServiceScopeFactory scopeFactory,
        Func<TService, TResult> operation)
        where TService : notnull
    {
        if (scopeFactory is null)
            throw new ArgumentNullException(nameof(scopeFactory));
        if (operation is null)
            throw new ArgumentNullException(nameof(operation));

        using var scope = scopeFactory.CreateScope();

        return operation(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
