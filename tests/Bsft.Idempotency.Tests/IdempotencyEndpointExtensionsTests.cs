using Bsft.Idempotency.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Bsft.Idempotency.Tests;

public class IdempotencyEndpointExtensionsTests
{
    [Fact]
    public void RequireIdempotency_MarksTheEndpointAndLeavesTheKeyDecisionToTheHost()
    {
        var recorder = new ConventionRecorder();

        var returned = recorder.RequireIdempotency();

        returned.Should().BeSameAs(recorder);
        recorder.Build().Metadata.GetMetadata<IIdempotencyMetadata>()!.ShouldRequireKey.Should().BeNull();
    }

    [Fact]
    public void RequireIdempotencyKey_MarksTheEndpointAndRefusesARequestWithoutAKey()
    {
        var recorder = new ConventionRecorder();

        var returned = recorder.RequireIdempotencyKey();

        returned.Should().BeSameAs(recorder);
        recorder.Build().Metadata.GetMetadata<IIdempotencyMetadata>()!.ShouldRequireKey.Should().BeTrue();
    }

    [Fact]
    public void RequireIdempotency_NullBuilder_IsRefused() =>
        FluentActions.Invoking(() => ((ConventionRecorder)null!).RequireIdempotency())
            .Should().Throw<ArgumentNullException>().WithParameterName("builder");

    [Fact]
    public void RequireIdempotencyKey_NullBuilder_IsRefused() =>
        FluentActions.Invoking(() => ((ConventionRecorder)null!).RequireIdempotencyKey())
            .Should().Throw<ArgumentNullException>().WithParameterName("builder");
}
