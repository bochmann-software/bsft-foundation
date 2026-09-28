using System.Collections.Concurrent;
using System.Text.Json;

namespace Bsft.EventAggregator.Saga;

/// <summary>
/// An in-memory implementation of <see cref="ISagaStore"/> backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Suitable for testing and single-process scenarios.
/// </summary>
/// <remarks>
/// State goes in and out through JSON, the same way a database-backed store carries it, rather than by
/// handing the caller's own object back. Without that the store would alias the instance a step is
/// mutating: a step that threw half way through would leave its half-applied changes sitting in what
/// the store calls the last committed state, and the next event would read them. Round-tripping also
/// means a state this store accepts is a state a real store accepts, so a type that cannot be
/// persisted fails in a test rather than on the first deployment that adds a database.
/// </remarks>
public sealed class InMemorySagaStore : ISagaStore
{
    private readonly ConcurrentDictionary<string, object> _instances = new();

    /// <inheritdoc />
    public Task<SagaInstance<TState>?> FindAsync<TState>(string sagaType, string correlationKey, CancellationToken cancellationToken = default)
    {
        if (sagaType is null)
            throw new ArgumentNullException(nameof(sagaType));
        if (correlationKey is null)
            throw new ArgumentNullException(nameof(correlationKey));

        var key = BuildKey(sagaType, correlationKey);
        var result = _instances.TryGetValue(key, out var value) && value is Stored<TState> stored
            ? stored.Rehydrate()
            : null;

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task SaveAsync<TState>(SagaInstance<TState> instance, CancellationToken cancellationToken = default)
    {
        if (instance is null)
            throw new ArgumentNullException(nameof(instance));

        var key = BuildKey(instance.SagaType, instance.CorrelationKey ?? string.Empty);
        _instances[key] = Stored<TState>.Of(instance);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string sagaType, string correlationKey, CancellationToken cancellationToken = default)
    {
        if (sagaType is null)
            throw new ArgumentNullException(nameof(sagaType));
        if (correlationKey is null)
            throw new ArgumentNullException(nameof(correlationKey));

        var key = BuildKey(sagaType, correlationKey);
        _instances.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private static string BuildKey(string sagaType, string correlationKey) => $"{sagaType}::{correlationKey}";

    /// <summary>
    /// One saved instance: its progress as values, its state as the JSON a real store would have
    /// written, so that neither the saver nor a later reader shares an object with this store.
    /// </summary>
    private sealed record Stored<TState>(
        string SagaType,
        string? CorrelationKey,
        string StateJson,
        int CurrentStepIndex,
        bool IsComplete,
        Guid Version)
    {
        public static Stored<TState> Of(SagaInstance<TState> instance) =>
            new(
                instance.SagaType,
                instance.CorrelationKey,
                JsonSerializer.Serialize(instance.State),
                instance.CurrentStepIndex,
                instance.IsComplete,
                instance.Version);

        public SagaInstance<TState> Rehydrate() =>
            new(SagaType, CorrelationKey, JsonSerializer.Deserialize<TState>(StateJson)!)
            {
                CurrentStepIndex = CurrentStepIndex,
                IsComplete = IsComplete,
                Version = Version,
            };
    }
}
