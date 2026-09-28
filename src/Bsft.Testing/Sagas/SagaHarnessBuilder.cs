using Bsft.EventAggregator.Saga;
using Bsft.Testing.Time;
using Microsoft.Extensions.DependencyInjection;
using Bsft.EventAggregator.DependencyInjection;
using Bsft.EventAggregator.Saga.DependencyInjection;
using Bsft.Testing.Contexts;

namespace Bsft.Testing.Sagas;

/// <summary>
/// The default <see cref="ISagaHarnessBuilder"/>.
/// </summary>
/// <remarks>
/// The test clock is registered before the engine, because the engine only supplies
/// <see cref="TimeProvider.System"/> where nothing else has claimed the registration. That single
/// ordering rule is what makes every durable timer in the engine virtual.
/// </remarks>
public sealed class SagaHarnessBuilder : ISagaHarnessBuilder
{
    private readonly List<Action<IServiceCollection>> _registrations = [];
    private readonly List<Action<BsftSagaOptions>> _engineConfigurations = [];
    private DateTimeOffset _startingAt = DateTimeOffset.UnixEpoch;

    /// <inheritdoc />
    public ISagaHarnessBuilder WithFlow<TFlow>()
        where TFlow : class, ISagaFlow =>
        WithServices(services => services.AddBsftSagaFlow<TFlow>());

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instance"/> is null.</exception>
    public ISagaHarnessBuilder WithService<TService>(TService instance)
        where TService : class
    {
        _registrations.AddInstance(instance);
        return this;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is null.</exception>
    public ISagaHarnessBuilder WithServices(Action<IServiceCollection> configure)
    {
        _registrations.AddConfiguration(configure);
        return this;
    }

    /// <inheritdoc />
    public ISagaHarnessBuilder WithTestContexts()
    {
        _registrations.AddConfiguration(TestContextRegistrations.ReplaceAllWithDefaults);
        return this;
    }

    /// <inheritdoc />
    public ISagaHarnessBuilder StartingAt(DateTimeOffset utcNow)
    {
        _startingAt = utcNow;
        return this;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is null.</exception>
    public ISagaHarnessBuilder ConfigureEngine(Action<BsftSagaOptions> configure)
    {
        if (configure is null)
            throw new ArgumentNullException(nameof(configure));

        _engineConfigurations.Add(configure);
        return this;
    }

    /// <inheritdoc />
    public ISagaTestHarness Build()
    {
        var clock = new TestClock(_startingAt);
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<ITestClock>(clock);
        services.AddBsftEventAggregator();
        services.AddBsftSaga(ApplyEngineConfigurations);

        _registrations.ApplyTo(services);

        var provider = services.BuildServiceProvider(validateScopes: true);
        provider.UseBsftSagaFlows();

        return new SagaTestHarness(provider, clock);
    }

    private void ApplyEngineConfigurations(BsftSagaOptions options)
    {
        foreach (var configure in _engineConfigurations)
        {
            configure(options);
        }
    }
}
