using Bsft.Correlation;
using FluentAssertions;
using Xunit;

namespace Bsft.Scheduling.Correlation.Tests;

public class CorrelationContextJobAccessorTests
{
    [Fact]
    public void ConstructorRejectsANullCorrelationContext()
    {
        var act = () => new CorrelationContextJobAccessor(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("correlationContext");
    }

    [Fact]
    public void OutsideAJobTheScopesOwnCorrelationIdIsUsed()
    {
        var accessor = new CorrelationContextJobAccessor(new CorrelationContext { CorrelationId = "request-1" });

        accessor.CorrelationId.Should().Be("request-1");
    }

    [Fact]
    public void InsideAJobTheAmbientIdWinsOverTheScopesOwnId()
    {
        var accessor = new CorrelationContextJobAccessor(new CorrelationContext { CorrelationId = "fresh-scope-id" });

        // A job enqueuing the next job must not start a new correlation on every hop, even though the
        // run's own service scope mints a fresh id.
        using (JobCorrelationScope.Use("job-chain-1"))
        {
            accessor.CorrelationId.Should().Be("job-chain-1");
        }

        accessor.CorrelationId.Should().Be("fresh-scope-id", "the ambient id only applies while a job runs");
    }

    [Fact]
    public void AnAmbientIdOfNullFallsBackToTheScopesOwnId()
    {
        var accessor = new CorrelationContextJobAccessor(new CorrelationContext { CorrelationId = "request-1" });

        using (JobCorrelationScope.Use(null))
        {
            accessor.CorrelationId.Should().Be("request-1");
        }
    }
}
