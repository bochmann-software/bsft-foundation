using Bsft.EventAggregator.Saga;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Testing.Sagas;

/// <summary>
/// Assembles a saga engine holding only the flows a test is about, on a clock the test owns.
/// </summary>
public interface ISagaHarnessBuilder
{
    /// <summary>Adds a saga flow to the engine.</summary>
    /// <typeparam name="TFlow">The flow to register.</typeparam>
    /// <returns>The same builder, for chaining.</returns>
    ISagaHarnessBuilder WithFlow<TFlow>()
        where TFlow : class, ISagaFlow;

    /// <summary>Registers a collaborator a saga step resolves, such as a mock.</summary>
    /// <typeparam name="TService">The service type to resolve the instance as.</typeparam>
    /// <param name="instance">The instance to register.</param>
    /// <returns>The same builder, for chaining.</returns>
    ISagaHarnessBuilder WithService<TService>(TService instance)
        where TService : class;

    /// <summary>Registers services that need more than a single instance registration.</summary>
    /// <param name="configure">Applied to the container while it is being assembled.</param>
    /// <returns>The same builder, for chaining.</returns>
    ISagaHarnessBuilder WithServices(Action<IServiceCollection> configure);

    /// <summary>
    /// Registers the tenant and correlation test doubles, for saga steps that depend on them.
    /// </summary>
    /// <returns>The same builder, for chaining.</returns>
    ISagaHarnessBuilder WithTestContexts();

    /// <summary>
    /// Sets the instant the clock starts at. Defaults to <see cref="DateTimeOffset.UnixEpoch"/>.
    /// </summary>
    /// <param name="utcNow">The starting instant.</param>
    /// <returns>The same builder, for chaining.</returns>
    ISagaHarnessBuilder StartingAt(DateTimeOffset utcNow);

    /// <summary>Configures the engine, for example its timeout batch size or catch-up policy.</summary>
    /// <param name="configure">Applied to the engine options.</param>
    /// <returns>The same builder, for chaining.</returns>
    ISagaHarnessBuilder ConfigureEngine(Action<BsftSagaOptions> configure);

    /// <summary>Builds the container, starts the flows, and returns the harness.</summary>
    /// <returns>The assembled harness, which the caller must dispose.</returns>
    ISagaTestHarness Build();
}
