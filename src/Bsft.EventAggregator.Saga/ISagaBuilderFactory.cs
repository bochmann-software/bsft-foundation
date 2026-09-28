namespace Bsft.EventAggregator.Saga;

/// <summary>
/// Factory for creating <see cref="SagaBuilder{TState}"/> instances and tracking built definitions.
/// </summary>
/// <remarks>
/// The tracking half lives on <see cref="ISagaDefinitionRegistry"/>, which this interface extends, so
/// callers reading <see cref="ISagaDefinitionRegistry.Definitions"/> through the factory are unchanged.
/// </remarks>
public interface ISagaBuilderFactory : ISagaDefinitionRegistry
{
    /// <summary>
    /// Creates a new saga builder with the specified state type.
    /// </summary>
    /// <typeparam name="TState">The type of the saga state. Must have a parameterless constructor.</typeparam>
    /// <returns>A new saga builder for defining the saga steps.</returns>
    SagaBuilder<TState> CreateBuilder<TState>() where TState : new();

    /// <summary>
    /// Creates a new saga builder with the specified state type.
    /// </summary>
    /// <typeparam name="TState">The type of the saga state. Must have a parameterless constructor.</typeparam>
    /// <returns>A new saga builder for defining the saga steps.</returns>
    SagaBuilder<TState> WithState<TState>() where TState : new();

    /// <summary>
    /// Creates a new saga builder with <see cref="EmptyState"/> for stateless sagas.
    /// </summary>
    /// <returns>A new saga builder using <see cref="EmptyState"/>.</returns>
    SagaBuilder<EmptyState> Stateless();
}
