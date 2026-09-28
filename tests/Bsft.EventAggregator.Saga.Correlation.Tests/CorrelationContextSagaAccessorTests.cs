using Bsft.Correlation;
using Bsft.EventAggregator.Saga.Correlation.DependencyInjection;
using Bsft.EventAggregator.Saga.Timeouts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.Correlation.Tests;

public class CorrelationContextSagaAccessorTests : IDisposable
{
    private readonly List<IDisposable> _disposables = [];

    [Fact]
    public void WithoutAFiringTimeout_TheScopeCorrelationIdIsUsed()
    {
        var (accessor, _) = Build();

        accessor.CorrelationId.Should().Be("request-1");
    }

    [Fact]
    public void WhileATimeoutIsFiring_ItsCorrelationIdWins()
    {
        // The scope comes out of the same container as the accessor, so both sides see one instance —
        // exactly what the singleton registration guarantees at runtime.
        var (accessor, scope) = Build();

        using (scope.Use("from-timeout"))
        {
            accessor.CorrelationId.Should().Be("from-timeout");
        }

        accessor.CorrelationId.Should().Be("request-1");
    }

    [Fact]
    public void AccessorRequiresACorrelationContext()
    {
        var services = new ServiceCollection();
        services.AddBsftSagaCorrelation();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<ISagaCorrelationAccessor>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddSagaCorrelation_RejectsANullServiceCollection()
    {
        var act = () => SagaCorrelationServiceCollectionExtensions.AddBsftSagaCorrelation(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddSagaCorrelation_DoesNotReplaceAnExistingAccessor()
    {
        var services = new ServiceCollection();
        services.AddScoped<ISagaCorrelationAccessor, StubAccessor>();
        services.AddBsftSagaCorrelation();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        scope.ServiceProvider.GetRequiredService<ISagaCorrelationAccessor>().Should().BeOfType<StubAccessor>();
    }

    private (ISagaCorrelationAccessor Accessor, ISagaCorrelationScope CorrelationScope) Build()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICorrelationContext>(_ => new CorrelationContext { CorrelationId = "request-1" });
        services.AddBsftSagaCorrelation();

        var sp = services.BuildServiceProvider();
        var scope = sp.CreateScope();
        _disposables.Add(scope);
        _disposables.Add(sp);

        return (
            scope.ServiceProvider.GetRequiredService<ISagaCorrelationAccessor>(),
            scope.ServiceProvider.GetRequiredService<ISagaCorrelationScope>());
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private sealed class StubAccessor : ISagaCorrelationAccessor
    {
        public string? CorrelationId => "stub";
    }
}
