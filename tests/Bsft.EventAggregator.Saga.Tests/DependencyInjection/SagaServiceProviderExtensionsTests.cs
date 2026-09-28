using Bsft.EventAggregator.Saga.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests.DependencyInjection;

public class SagaServiceProviderExtensionsTests
{
    [Fact]
    public void UseBsftSagaFlows_NullServices_ThrowsArgumentNullException()
    {
        IServiceProvider services = null!;

        var act = () => services.UseBsftSagaFlows();

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }
}
