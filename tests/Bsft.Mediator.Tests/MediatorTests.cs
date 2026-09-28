using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

public class MediatorTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public MediatorTests()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator, DefaultMediator>();
        services.AddTransient<ICommandHandler<TestCommand, string>, TestCommandHandler>();
        services.AddTransient<ICommandHandler<TestVoidCommand>, TestVoidCommandHandler>();
        services.AddTransient<IQueryHandler<TestQuery, string>, TestQueryHandler>();
        services.AddTransient<INotificationHandler<TestNotification>, TestNotificationHandler1>();
        services.AddTransient<INotificationHandler<TestNotification>, TestNotificationHandler2>();

        _serviceProvider = services.BuildServiceProvider();

        TestVoidCommandHandler.Reset();
        TestNotificationHandler1.Reset();
        TestNotificationHandler2.Reset();
        TestPipelineBehavior<object, object>.Reset();
    }

    [Fact]
    public async Task SendAsync_WithResultCommand_ReturnsHandlerResult()
    {
        var mediator = _serviceProvider.GetRequiredService<IMediator>();
        var command = new TestCommand("test");

        var result = await mediator.SendAsync<string>(command);

        result.Should().Be("Handled: test");
    }

    [Fact]
    public async Task SendAsync_WithVoidCommand_CallsHandler()
    {
        var mediator = _serviceProvider.GetRequiredService<IMediator>();
        var command = new TestVoidCommand("test");

        await mediator.SendAsync(command);

        TestVoidCommandHandler.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task QueryAsync_ReturnsHandlerResult()
    {
        var mediator = _serviceProvider.GetRequiredService<IMediator>();
        var query = new TestQuery(42);

        var result = await mediator.QueryAsync<string>(query);

        result.Should().Be("Result: 42");
    }

    [Fact]
    public async Task PublishAsync_CallsAllNotificationHandlers()
    {
        var mediator = _serviceProvider.GetRequiredService<IMediator>();
        var notification = new TestNotification("hello");

        await mediator.PublishAsync(notification);

        TestNotificationHandler1.Messages.Should().ContainSingle().Which.Should().Be("Handler1: hello");
        TestNotificationHandler2.Messages.Should().ContainSingle().Which.Should().Be("Handler2: hello");
    }

    [Fact]
    public async Task SendAsync_WithNoRegisteredHandler_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator, DefaultMediator>();
        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var act = () => mediator.SendAsync<string>(new TestCommand("test"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No handler registered*");
    }

    [Fact]
    public async Task QueryAsync_WithNoRegisteredHandler_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator, DefaultMediator>();
        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var act = () => mediator.QueryAsync<string>(new TestQuery(1));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No handler registered*");
    }

    [Fact]
    public async Task SendAsync_WithPipelineBehavior_ExecutesBehaviorAroundHandler()
    {
        // The log lives on the closed generic the pipeline actually instantiates, which is the
        // static request type the mediator dispatches under, not TestPipelineBehavior<object, object>.
        TestPipelineBehavior<ICommand<string>, string>.Reset();

        var services = new ServiceCollection();
        services.AddScoped<IMediator, DefaultMediator>();
        services.AddTransient<ICommandHandler<TestCommand, string>, TestCommandHandler>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TestPipelineBehavior<,>));
        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var result = await mediator.SendAsync<string>(new TestCommand("pipeline"));

        result.Should().Be("Handled: pipeline");
        TestPipelineBehavior<ICommand<string>, string>.ExecutionLog.Should().HaveCount(2);
        TestPipelineBehavior<ICommand<string>, string>.ExecutionLog[0].Should().StartWith("Before:");
        TestPipelineBehavior<ICommand<string>, string>.ExecutionLog[1].Should().StartWith("After:");
    }

    [Fact]
    public async Task SendAsync_WithNullCommand_ThrowsArgumentNullException()
    {
        var mediator = _serviceProvider.GetRequiredService<IMediator>();

        var act = () => mediator.SendAsync<string>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task QueryAsync_WithNullQuery_ThrowsArgumentNullException()
    {
        var mediator = _serviceProvider.GetRequiredService<IMediator>();

        var act = () => mediator.QueryAsync<string>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PublishAsync_WithNullNotification_ThrowsArgumentNullException()
    {
        var mediator = _serviceProvider.GetRequiredService<IMediator>();

        var act = () => mediator.PublishAsync<TestNotification>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PublishAsync_WithNoHandlers_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator, DefaultMediator>();
        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var act = () => mediator.PublishAsync(new TestNotification("no handlers"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAsync_VoidCommand_WithUnitHandler_FallsBackToUnitHandler()
    {
        // Exercises Mediator.cs lines 77-78 (<SendAsync>g__InvokeUnitHandler):
        // When sending an ICommand (void) and no ICommandHandler<T> is registered,
        // it falls back to ICommandHandler<T, Unit>.
        var services = new ServiceCollection();
        services.AddScoped<IMediator, DefaultMediator>();
        // Register ICommandHandler<TestVoidCommand, Unit> instead of ICommandHandler<TestVoidCommand>
        services.AddTransient<ICommandHandler<TestVoidCommand, global::Bsft.Core.Unit>, TestVoidCommandUnitHandler>();
        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        await mediator.SendAsync(new TestVoidCommand("fallback"));

        TestVoidCommandUnitHandler.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_VoidCommand_WithNoHandler_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator, DefaultMediator>();
        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var act = () => mediator.SendAsync((ICommand) new TestVoidCommand("none"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No handler registered*");
    }

    [Fact]
    public async Task SendAsync_VoidCommand_WithNullCommand_ThrowsArgumentNullException()
    {
        // Exercises Mediator.SendAsync(ICommand) line 48: null guard for ICommand overload.
        var mediator = _serviceProvider.GetRequiredService<IMediator>();

        var act = () => mediator.SendAsync((ICommand)null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("command");
    }

    [Fact]
    public async Task PublishAsync_OneHandlerThrows_InvokesAllAndAggregatesFailures()
    {
        IsolationRecorderHandler1.Reset();
        IsolationRecorderHandler2.Reset();

        var services = new ServiceCollection();
        services.AddScoped<IMediator, DefaultMediator>();
        services.AddTransient<INotificationHandler<IsolationNotification>, IsolationRecorderHandler1>();
        services.AddTransient<INotificationHandler<IsolationNotification>, IsolationThrowingHandler>();
        services.AddTransient<INotificationHandler<IsolationNotification>, IsolationRecorderHandler2>();
        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var act = async () => await mediator.PublishAsync(new IsolationNotification("isolate"));

        var assertion = await act.Should().ThrowAsync<AggregateException>();
        assertion.Which.InnerExceptions.Should().ContainSingle()
            .Which.Should().BeOfType<InvalidOperationException>();
        // Handlers registered before and after the faulting one are still invoked.
        IsolationRecorderHandler1.Messages.Should().ContainSingle().Which.Should().Be("Recorder1: isolate");
        IsolationRecorderHandler2.Messages.Should().ContainSingle().Which.Should().Be("Recorder2: isolate");
    }

    [Fact]
    public async Task PublishAsync_TwoHandlersThrow_AggregatesBothFailuresInRegistrationOrder()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator, DefaultMediator>();
        services.AddTransient<INotificationHandler<DoubleFailureNotification>, FirstFailingHandler>();
        services.AddTransient<INotificationHandler<DoubleFailureNotification>, SecondFailingHandler>();
        using var sp = services.BuildServiceProvider();

        var act = () => sp.GetRequiredService<IMediator>().PublishAsync(new DoubleFailureNotification());

        var assertion = await act.Should().ThrowAsync<AggregateException>();
        assertion.Which.InnerExceptions.Select(exception => exception.Message)
            .Should().Equal("first handler failed", "second handler failed");
    }

    [Theory]
    [InlineData(false, "Complete")]
    [InlineData(true, "Fail")]
    public async Task PublishAsync_WithTracing_ReportsTheOutcomeToTheHostsTelemetry(bool handlerFails, string outcome)
    {
        // The telemetry is a seam a host may fill with its own emitter; it learns how the publish
        // ended only from the scope, so the outcome has to be reported before the scope is disposed.
        var telemetry = new RecordingMediatorTelemetry();
        var services = new ServiceCollection();
        services.AddSingleton<IMediatorTelemetry>(telemetry);
        services.AddBsftMediator(cfg => cfg.EnableTracing());
        if (handlerFails)
            services.AddTransient<INotificationHandler<DoubleFailureNotification>, FirstFailingHandler>();

        using var sp = services.BuildServiceProvider();

        var act = () => sp.GetRequiredService<IMediator>().PublishAsync(new DoubleFailureNotification());

        if (handlerFails)
            await act.Should().ThrowAsync<AggregateException>();
        else
            await act.Should().NotThrowAsync();

        telemetry.Calls.Should().Equal($"BeginPublish:{nameof(DoubleFailureNotification)}", outcome, "Dispose");
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }
}
