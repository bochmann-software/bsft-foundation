using Microsoft.Extensions.DependencyInjection;

namespace Bsft.EventAggregator.Saga;

/// <summary>
/// Creates the service scope a saga step runs in.
/// </summary>
public interface ISagaStepScopeFactory
{
    /// <summary>
    /// Creates a scope for one saga step, carrying the correlation of whatever caused the step.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe while creating the scope.</param>
    /// <returns>
    /// The scope, which the caller disposes when the step has finished. The scope is returned as an
    /// <see cref="AsyncServiceScope"/> so the caller can <c>await using</c> it: a step routinely
    /// resolves async-disposable scoped services (an EF Core <c>DbContext</c> among them), and only
    /// asynchronous disposal tears those down deterministically once the awaited step has completed.
    /// The factory itself is asynchronous so that when correlation restoration fails it can dispose
    /// the half-built scope through <see cref="AsyncServiceScope.DisposeAsync"/> rather than a
    /// synchronous <c>Dispose</c>, which would throw on an async-only-disposable scoped service and
    /// mask the original failure.
    /// </returns>
    ValueTask<AsyncServiceScope> CreateAsync(CancellationToken cancellationToken = default);
}
