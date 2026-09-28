using Bsft.Mediator;
using Bsft.Testing.Contexts;
using Bsft.Testing.Mediation;
using Bsft.Testing.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Testing.Tests.Mediation;

public class MediatorHarnessTests
{
    [Fact]
    public async Task Command_with_a_result_reaches_its_handler()
    {
        using var harness = new MediatorHarnessBuilder()
            .WithHandler<AddNumbersHandler>()
            .Build();

        (await harness.SendAsync(new AddNumbers(2, 3))).Should().Be(5);
    }

    [Fact]
    public async Task Command_without_a_result_reaches_its_handler()
    {
        var trace = new Trace();
        using var harness = new MediatorHarnessBuilder()
            .WithService<ITrace>(trace)
            .WithHandler<RecordNoteHandler>()
            .Build();

        await harness.SendAsync(new RecordNote("first"));

        trace.Entries.Should().ContainSingle().Which.Should().Be("handled:first");
    }

    [Fact]
    public void Non_handler_interfaces_of_a_handler_are_left_alone()
    {
        using var harness = new MediatorHarnessBuilder()
            .WithHandler<AddNumbersHandler>()
            .Build();

        harness.Services.GetService<ICommandHandler<AddNumbers, int>>().Should().NotBeNull();
        harness.Services.GetService<IAuditedHandler>().Should().BeNull();
    }

    [Fact]
    public async Task Query_reaches_its_handler()
    {
        using var harness = new MediatorHarnessBuilder()
            .WithHandler(typeof(GetGreetingHandler))
            .Build();

        (await harness.QueryAsync(new GetGreeting("world"))).Should().Be("hello world");
    }

    [Fact]
    public async Task Notification_reaches_its_handler()
    {
        var trace = new Trace();
        using var harness = new MediatorHarnessBuilder()
            .WithService<ITrace>(trace)
            .WithHandler<NoteRecordedHandler>()
            .Build();

        await harness.PublishAsync(new NoteRecorded("published"));

        trace.Entries.Should().ContainSingle().Which.Should().Be("notified:published");
    }

    [Fact]
    public async Task Behaviors_run_in_the_order_they_were_added()
    {
        var trace = new Trace();
        using var harness = new MediatorHarnessBuilder()
            .WithService<ITrace>(trace)
            .WithHandler<AddNumbersHandler>()
            .WithBehavior(typeof(OuterBehavior<,>))
            .WithBehavior(typeof(InnerBehavior<,>))
            .Build();

        await harness.SendAsync(new AddNumbers(1, 1));

        trace.Entries.Should().Equal("outer:before", "inner:before", "inner:after", "outer:after");
    }

    [Fact]
    public async Task Behavior_closed_over_the_request_contract_still_takes_part()
    {
        using var harness = new MediatorHarnessBuilder()
            .WithHandler<AddNumbersHandler>()
            .WithBehavior(typeof(DoublingBehavior))
            .Build();

        (await harness.SendAsync(new AddNumbers(2, 3))).Should().Be(10);
    }

    [Fact]
    public async Task Pipeline_contains_only_what_the_test_asked_for()
    {
        var trace = new Trace();
        using var harness = new MediatorHarnessBuilder()
            .WithService<ITrace>(trace)
            .WithHandler<AddNumbersHandler>()
            .Build();

        await harness.SendAsync(new AddNumbers(1, 1));

        trace.Entries.Should().BeEmpty();
        harness.Services.GetServices<IPipelineBehavior<ICommand<int>, int>>().Should().BeEmpty();
    }

    [Fact]
    public async Task Test_contexts_are_available_to_a_handler()
    {
        using var harness = new MediatorHarnessBuilder()
            .WithTestContexts()
            .WithHandler<WhichTenantHandler>()
            .Build();

        (await harness.QueryAsync(new WhichTenant())).Should().Be(TestTenantContext.DefaultTenantId);
    }

    [Fact]
    public async Task Extra_registrations_reach_the_container()
    {
        using var harness = new MediatorHarnessBuilder()
            .WithServices(services => services.AddSingleton<ITrace, Trace>())
            .WithHandler<RecordNoteHandler>()
            .Build();

        await harness.SendAsync(new RecordNote("via-configure"));

        harness.Services.GetRequiredService<ITrace>().Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task Unregistered_handler_fails_loudly()
    {
        using var harness = new MediatorHarnessBuilder().Build();

        var send = async () => await harness.SendAsync(new UnhandledCommand());

        await send.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Mediator_is_exposed_for_direct_use()
    {
        using var harness = new MediatorHarnessBuilder().Build();

        harness.Mediator.Should().BeSameAs(harness.Services.GetRequiredService<IMediator>());
    }

    [Fact]
    public void Disposing_the_harness_disposes_the_container()
    {
        var harness = new MediatorHarnessBuilder().Build();
        var services = harness.Services;

        harness.Dispose();

        var resolve = () => services.GetRequiredService<IMediator>();
        resolve.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Harness_rejects_a_missing_container()
    {
        var create = () => new MediatorTestHarness(null!);

        create.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void Handler_type_must_be_a_concrete_class()
    {
        var builder = new MediatorHarnessBuilder();

        var register = () => builder.WithHandler(typeof(AbstractHandler));

        register.Should().Throw<ArgumentException>().WithParameterName("handlerType");
    }

    [Fact]
    public void Handler_type_must_implement_a_handler_interface()
    {
        var builder = new MediatorHarnessBuilder();

        var register = () => builder.WithHandler<NotAHandler>();

        register.Should().Throw<ArgumentException>().WithParameterName("handlerType");
    }

    [Fact]
    public void Behavior_type_must_be_a_concrete_class()
    {
        var builder = new MediatorHarnessBuilder();

        var register = () => builder.WithBehavior(typeof(IPipelineBehavior<,>));

        register.Should().Throw<ArgumentException>().WithParameterName("behaviorType");
    }

    [Fact]
    public void Behavior_type_must_implement_the_behavior_interface()
    {
        var builder = new MediatorHarnessBuilder();

        var register = () => builder.WithBehavior(typeof(NotAHandler));

        register.Should().Throw<ArgumentException>().WithParameterName("behaviorType");
    }

    [Fact]
    public void Builder_rejects_null_arguments()
    {
        var builder = new MediatorHarnessBuilder();

        var calls = new List<Action>
        {
            () => builder.WithHandler(null!),
            () => builder.WithBehavior(null!),
            () => builder.WithService<ITrace>(null!),
            () => builder.WithServices(null!),
        };

        calls.Should().AllSatisfy(call => call.Should().Throw<ArgumentNullException>());
    }
}
