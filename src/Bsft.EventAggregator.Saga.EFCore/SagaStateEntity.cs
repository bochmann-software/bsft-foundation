namespace Bsft.EventAggregator.Saga.EFCore;

/// <summary>
/// EF Core entity for persisting saga instance state to a relational database.
/// </summary>
public sealed class SagaStateEntity
{
    /// <summary>Auto-increment primary key.</summary>
    public int Id { get; set; }

    /// <summary>The saga type identifier.</summary>
    public string SagaType { get; set; } = string.Empty;

    /// <summary>The correlation key for this saga instance.</summary>
    public string CorrelationKey { get; set; } = string.Empty;

    /// <summary>The CLR type name of the saga state (for deserialization).</summary>
    public string StateTypeName { get; set; } = string.Empty;

    /// <summary>The saga state serialized as JSON.</summary>
    public string StateJson { get; set; } = string.Empty;

    /// <summary>Current step index in the saga workflow.</summary>
    public int CurrentStepIndex { get; set; }

    /// <summary>Whether the saga instance has completed.</summary>
    public bool IsComplete { get; set; }

    /// <summary>When this saga instance was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When this saga instance was last updated.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Optimistic-concurrency token. A fresh value is written on every save; EF Core includes the
    /// previously loaded value in the UPDATE predicate, so a concurrent writer that already changed
    /// the row causes a <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>
    /// instead of a silent lost update.
    /// </summary>
    public Guid ConcurrencyToken { get; set; }
}
