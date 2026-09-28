using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Bsft.Mediator.Tests;

public sealed class TracingBehaviorTests
{
    private readonly IMediatorTelemetry _telemetry = Substitute.For<IMediatorTelemetry>();
    private readonly IRequestTelemetryScope _scope = Substitute.For<IRequestTelemetryScope>();

    public TracingBehaviorTests() =>
        _telemetry.BeginRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>()).Returns(_scope);

    [Fact]
    public void Constructor_NullTelemetry_Throws()
    {
        var act = () => new TracingBehavior<TestCommand, string>(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("telemetry");
    }

    [Fact]
    public async Task HandleAsync_NullNext_Throws()
    {
        var behavior = new TracingBehavior<TestCommand, string>(_telemetry);
        var act = () => behavior.HandleAsync(new TestCommand("x"), null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("next");
    }

    [Fact]
    public async Task HandleAsync_Success_CompletesScopeAndReturnsResponse()
    {
        var behavior = new TracingBehavior<TestCommand, string>(_telemetry);

        var result = await behavior.HandleAsync(new TestCommand("x"), () => Task.FromResult("ok"));

        result.Should().Be("ok");
        _scope.Received(1).Complete();
        _scope.DidNotReceive().Fail(Arg.Any<Exception>());
        _scope.Received(1).Dispose();
    }

    [Fact]
    public async Task HandleAsync_HandlerThrows_FailsScopeAndRethrows()
    {
        var behavior = new TracingBehavior<TestCommand, string>(_telemetry);
        var boom = new InvalidOperationException("boom");

        var act = () => behavior.HandleAsync(new TestCommand("x"), () => Task.FromException<string>(boom));

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(boom);
        _scope.Received(1).Fail(boom);
        _scope.DidNotReceive().Complete();
        _scope.Received(1).Dispose();
    }

    [Fact]
    public async Task HandleAsync_PassesCorrelationIdFromAccessor()
    {
        var accessor = Substitute.For<ICorrelationIdAccessor>();
        accessor.CorrelationId.Returns("corr-42");
        var behavior = new TracingBehavior<TestCommand, string>(_telemetry, accessor);

        await behavior.HandleAsync(new TestCommand("x"), () => Task.FromResult("ok"));

        _telemetry.Received(1).BeginRequest("TestCommand", typeof(TestCommand).ToString(), "corr-42");
    }

    [Fact]
    public async Task HandleAsync_DispatchedUnderTheCommandContract_NamesTheSpanAfterTheRequest()
    {
        // The mediator closes the pipeline over ICommand<TResult>, so a span named after the type
        // parameter would be the same span name for every command returning a string.
        var behavior = new TracingBehavior<ICommand<string>, string>(_telemetry);

        await behavior.HandleAsync(new TestCommand("x"), () => Task.FromResult("ok"));

        _telemetry.Received(1).BeginRequest(
            nameof(TestCommand), typeof(TestCommand).ToString(), Arg.Any<string?>());
    }
}
