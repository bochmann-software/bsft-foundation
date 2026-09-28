namespace Bsft.EventAggregator.Saga;

/// <summary>
/// Configuration options for the saga engine.
/// </summary>
public sealed class BsftSagaOptions
{
    /// <summary>
    /// The configuration section this options type binds from: <c>Bsft:Saga</c>.
    /// </summary>
    public const string SectionName = "Bsft:Saga";

    /// <summary>
    /// Gets or sets a value indicating whether a new DI scope should be created for each saga step
    /// execution. Defaults to <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Retained for source compatibility; it no longer changes anything. Both ways into a step —
    /// the event-driven flow host and <see cref="ISagaEngine.StartSagaAsync{TState}"/> — now always
    /// create one, because the framework resolves scoped services in a step itself and the root
    /// provider cannot serve them: under the scope validation an ASP.NET Core Development host turns
    /// on, doing so throws rather than degrading.
    /// </remarks>
    public bool ShouldCreateScopePerStep { get; set; }

    /// <summary>
    /// Gets the durable timeout options. Timeouts are always available; only the store differs
    /// between the in-memory and the persistent provider.
    /// </summary>
    public SagaTimeoutOptions Timeouts { get; } = new();
}
