namespace Bsft.EventAggregator.Saga;

/// <summary>
/// Collects the saga definitions a <see cref="SagaBuilder{TState}"/> completes, so the saga host can
/// subscribe to the events they start and continue on.
/// </summary>
/// <remarks>
/// Split from <see cref="ISagaBuilderFactory"/>, which extends it, so that a builder depends only on
/// where its finished definition goes and not on the factory that created it: the dependency runs from
/// the factory to the builder and to this registry, never back.
/// </remarks>
public interface ISagaDefinitionRegistry
{
    /// <summary>
    /// Gets all registered saga definitions.
    /// </summary>
    IReadOnlyList<object> Definitions { get; }

    /// <summary>
    /// Registers a completed saga definition for hosting.
    /// </summary>
    /// <typeparam name="TState">The type of the saga state.</typeparam>
    /// <param name="definition">The saga definition to register.</param>
    void Register<TState>(SagaDefinition<TState> definition);
}
