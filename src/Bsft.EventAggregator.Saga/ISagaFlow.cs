namespace Bsft.EventAggregator.Saga;

/// <summary>
/// Defines a saga flow that configures one or more saga definitions using the provided builder factory.
/// Implementations are resolved from DI and their <see cref="Configure"/> method is called during startup.
/// </summary>
public interface ISagaFlow
{
    /// <summary>
    /// Configures the saga definitions for this flow using the provided factory.
    /// </summary>
    /// <param name="factory">The factory for creating saga builders.</param>
    void Configure(ISagaBuilderFactory factory);
}
