namespace Bsft.EventAggregator.Saga.Execution;

/// <summary>
/// Factory for creating <see cref="SagaBuilder{TState}"/> instances, injectable via DI.
/// Tracks all built definitions so the <see cref="SagaFlowHost"/> can subscribe to the appropriate events.
/// </summary>
internal sealed class SagaBuilderFactory : ISagaBuilderFactory
{
    private readonly ISagaEngine _engine;
    private readonly List<object> _definitions = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaBuilderFactory"/> class.
    /// </summary>
    /// <param name="engine">The saga engine used by builders to start saga instances.</param>
    public SagaBuilderFactory(ISagaEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <summary>
    /// Creates a new saga builder with the specified state type.
    /// </summary>
    /// <typeparam name="TState">The type of the saga state. Must have a parameterless constructor.</typeparam>
    /// <returns>A new saga builder for defining the saga steps.</returns>
    public SagaBuilder<TState> CreateBuilder<TState>() where TState : new()
    {
        return new SagaBuilder<TState>(_engine, this);
    }

    /// <summary>
    /// Creates a new saga builder with the specified state type.
    /// </summary>
    /// <typeparam name="TState">The type of the saga state. Must have a parameterless constructor.</typeparam>
    /// <returns>A new saga builder for defining the saga steps.</returns>
    public SagaBuilder<TState> WithState<TState>() where TState : new()
    {
        return CreateBuilder<TState>();
    }

    /// <summary>
    /// Creates a new saga builder with <see cref="EmptyState"/> for stateless sagas.
    /// </summary>
    /// <returns>A new saga builder using <see cref="EmptyState"/>.</returns>
    public SagaBuilder<EmptyState> Stateless()
    {
        return CreateBuilder<EmptyState>();
    }

    /// <summary>
    /// Registers a completed saga definition for hosting by <see cref="SagaFlowHost"/>.
    /// </summary>
    /// <typeparam name="TState">The type of the saga state.</typeparam>
    /// <param name="definition">The saga definition to register.</param>
    /// <inheritdoc />
    public void Register<TState>(SagaDefinition<TState> definition)
    {
        _definitions.Add(definition);
    }

    /// <summary>
    /// Gets all registered saga definitions.
    /// </summary>
    /// <inheritdoc />
    public IReadOnlyList<object> Definitions => _definitions.AsReadOnly();
}
