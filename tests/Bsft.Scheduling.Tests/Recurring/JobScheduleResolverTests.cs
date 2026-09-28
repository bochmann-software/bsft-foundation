using Bsft.Scheduling.Recurring;
using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Scheduling.Tests.Recurring;

public sealed class JobScheduleResolverTests
{
    private static readonly JobSchedule Hourly = JobSchedule.Every(TimeSpan.FromHours(1));

    private static readonly JobDefinition Declared =
        new("declared", typeof(SelfSchedulingJob), Hourly, JobOverlapPolicy.Forbid);

    private static readonly JobDefinition SelfScheduling =
        new("self", typeof(SelfSchedulingJob), null, JobOverlapPolicy.Forbid);

    private readonly ServiceProvider _provider;

    public JobScheduleResolverTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new JobLog());
        services.AddScoped<SelfSchedulingJob>();
        _provider = services.BuildServiceProvider();
    }

    [Fact]
    public void AScheduleGivenAtRegistrationIsTheAnswer()
    {
        Build(Declared).Resolve("declared").Should().BeSameAs(Hourly);
    }

    [Fact]
    public void AJobThatDeclaresItsOwnScheduleIsResolvedOnceAndRemembered()
    {
        var resolver = Build(SelfScheduling);

        var first = resolver.Resolve("self");

        first.Should().NotBeNull();
        resolver.Resolve("self").Should().BeSameAs(first, "a job's schedule is a constant of its type");
    }

    [Fact]
    public void AnUnregisteredNameIsRejectedRatherThanAnsweredWithAGuess()
    {
        Build(Declared).Invoking(resolver => resolver.Resolve("never-registered"))
            .Should().Throw<JobNotRegisteredException>().WithMessage("*never-registered*");
    }

    [Fact]
    public void TheNullGuardsRejectMissingArguments()
    {
        ((Action)(() => new JobScheduleResolver(null!, _provider.GetRequiredService<IServiceScopeFactory>())))
            .Should().Throw<ArgumentNullException>().WithParameterName("registry");
        ((Action)(() => new JobScheduleResolver(new JobRegistry([Declared], JobOptionsMonitor.Defaults), null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");

        Build(Declared).Invoking(resolver => resolver.Resolve(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("jobName");
    }

    private JobScheduleResolver Build(params JobDefinition[] definitions) =>
        new(
            new JobRegistry(definitions, JobOptionsMonitor.Defaults),
            _provider.GetRequiredService<IServiceScopeFactory>());
}
