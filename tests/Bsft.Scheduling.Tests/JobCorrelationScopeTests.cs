using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Scheduling.Tests;

public class JobCorrelationScopeTests
{
    [Fact]
    public void NoScopeMeansNoAmbientCorrelationId()
    {
        JobCorrelationScope.Current.Should().BeNull();
    }

    [Fact]
    public void AScopeMakesTheIdAmbientAndRestoresThePreviousValue()
    {
        using (JobCorrelationScope.Use("corr-1"))
        {
            JobCorrelationScope.Current.Should().Be("corr-1");
        }

        JobCorrelationScope.Current.Should().BeNull();
    }

    [Fact]
    public void ScopesNestAndUnwindInOrder()
    {
        // A job that enqueues another job must not leak the inner id back to the outer one.
        using (JobCorrelationScope.Use("outer"))
        {
            using (JobCorrelationScope.Use("inner"))
            {
                JobCorrelationScope.Current.Should().Be("inner");
            }

            JobCorrelationScope.Current.Should().Be("outer");
        }
    }

    [Fact]
    public void ANullIdIsAValidScope()
    {
        using (JobCorrelationScope.Use("corr-1"))
        {
            using (JobCorrelationScope.Use(null))
            {
                JobCorrelationScope.Current.Should().BeNull();
            }

            JobCorrelationScope.Current.Should().Be("corr-1");
        }
    }

    [Fact]
    public async Task TheIdFlowsIntoAnAwaitedContinuation()
    {
        using (JobCorrelationScope.Use("corr-1"))
        {
            await Task.Yield();

            JobCorrelationScope.Current.Should().Be("corr-1");
        }
    }

    [Fact]
    public void TheAmbientAccessorReportsTheCurrentScope()
    {
        var accessor = new AmbientJobCorrelationAccessor();

        accessor.CorrelationId.Should().BeNull();

        using (JobCorrelationScope.Use("corr-1"))
        {
            accessor.CorrelationId.Should().Be("corr-1");
        }
    }
}
