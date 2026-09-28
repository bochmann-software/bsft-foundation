using Microsoft.EntityFrameworkCore;

namespace Bsft.EventAggregator.Saga.EFCore;

/// <summary>
/// Contract for a DbContext that additionally persists durable saga timeouts.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="ISagaDbContext"/>. Durable timeouts are an opt-in
/// capability, and adding their table to the state contract would force every existing
/// implementation to change for a feature it may not use.
/// </remarks>
public interface ISagaTimeoutDbContext : ISagaDbContext
{
    /// <summary>
    /// Gets the set of pending and dispatched saga timeouts.
    /// </summary>
    DbSet<SagaTimeoutEntity> SagaTimeouts { get; }
}
