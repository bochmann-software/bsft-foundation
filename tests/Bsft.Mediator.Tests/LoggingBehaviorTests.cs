using Bsft.Mediator.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Bsft.Mediator.Tests;

public class LoggingBehaviorTests
{
    private readonly ILogger<LoggingBehavior<TestCommand, string>> _logger;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;
    private readonly LoggingBehavior<TestCommand, string> _sut;

    public LoggingBehaviorTests()
    {
        _logger = Substitute.For<ILogger<LoggingBehavior<TestCommand, string>>>();
        _correlationIdAccessor = Substitute.For<ICorrelationIdAccessor>();
        _correlationIdAccessor.CorrelationId.Returns("test-correlation-id");
        _sut = new LoggingBehavior<TestCommand, string>(_logger, _correlationIdAccessor);
    }

    [Fact]
    public async Task HandleAsync_OnSuccess_LogsStartAndCompletion()
    {
        var command = new TestCommand("test");

        var result = await _sut.HandleAsync(
            command,
            () => Task.FromResult("ok"));

        result.Should().Be("ok");
        _logger.ReceivedWithAnyArgs(2).Log(
            default,
            default,
            default(object)!,
            default,
            default!);
    }

    [Fact]
    public async Task HandleAsync_OnFailure_LogsError()
    {
        var command = new TestCommand("test");

        var act = () => _sut.HandleAsync(
            command,
            () => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        _logger.ReceivedWithAnyArgs(2).Log(
            default,
            default,
            default(object)!,
            default,
            default!);
    }

    [Fact]
    public async Task HandleAsync_IncludesCorrelationIdInLog()
    {
        var command = new TestCommand("test");

        await _sut.HandleAsync(command, () => Task.FromResult("ok"));

        var _ = _correlationIdAccessor.Received().CorrelationId;
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var act = () => new LoggingBehavior<TestCommand, string>(null!, _correlationIdAccessor);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task HandleAsync_WithoutCorrelationIdAccessor_UsesPlaceholder()
    {
        var sut = new LoggingBehavior<TestCommand, string>(_logger);
        var command = new TestCommand("test");

        var result = await sut.HandleAsync(command, () => Task.FromResult("ok"));

        result.Should().Be("ok");
        _logger.ReceivedWithAnyArgs(2).Log(
            default,
            default,
            default(object)!,
            default,
            default!);
    }

    [Fact]
    public async Task HandleAsync_DispatchedUnderTheCommandContract_LogsTheConcreteRequestName()
    {
        // The mediator closes the pipeline over ICommand<TResult>, so a behavior that logged its own
        // type parameter would give every command of one result type the same, useless name.
        var logger = new RecordingLogger<LoggingBehavior<ICommand<string>, string>>();
        var sut = new LoggingBehavior<ICommand<string>, string>(logger, _correlationIdAccessor);

        await sut.HandleAsync(new TestCommand("test"), () => Task.FromResult("ok"));

        logger.Messages.Should().OnlyContain(message => message.Contains(nameof(TestCommand)));
    }

    [Theory]
    [InlineData("order-42.retry_1", "[order-42.retry_1]")]
    [InlineData("abc\r\nHandled Something", "[-]")]
    [InlineData(null, "[-]")]
    public async Task HandleAsync_ByCorrelationIdShape_LogsTheIdOnlyWhenItIsWellFormed(
        string? correlationId, string expectedPrefix)
    {
        var logger = new RecordingLogger<LoggingBehavior<TestCommand, string>>();
        var accessor = Substitute.For<ICorrelationIdAccessor>();
        accessor.CorrelationId.Returns(correlationId);
        var sut = new LoggingBehavior<TestCommand, string>(logger, accessor);

        await sut.HandleAsync(new TestCommand("test"), () => Task.FromResult("ok"));

        logger.Messages.Should().HaveCount(2)
            .And.OnlyContain(message => message.StartsWith(expectedPrefix + " ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleAsync_WithAMalformedCorrelationId_LogsThePlaceholderInstead()
    {
        // The accessor is a public seam, so the value need not have passed the middleware that
        // sanitizes an incoming header. A caller must not be able to write its own log lines.
        var logger = new RecordingLogger<LoggingBehavior<TestCommand, string>>();
        var accessor = Substitute.For<ICorrelationIdAccessor>();
        accessor.CorrelationId.Returns("abc\r\nHandled Something");
        var sut = new LoggingBehavior<TestCommand, string>(logger, accessor);

        await sut.HandleAsync(new TestCommand("test"), () => Task.FromResult("ok"));

        logger.Messages.Should().OnlyContain(message => message.StartsWith("[-]", StringComparison.Ordinal));
    }
}
