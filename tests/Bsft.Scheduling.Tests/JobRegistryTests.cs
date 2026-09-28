using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Scheduling.Tests;

public class JobRegistryTests
{
    private static readonly JobDefinition _report =
        new("report", typeof(RecordingJob), null, JobOverlapPolicy.Forbid);

    private static readonly JobDefinition _nightly =
        new("nightly", typeof(FailingJob), JobSchedule.Every(TimeSpan.FromHours(1)), JobOverlapPolicy.Allow);

    [Fact]
    public void ARegistrationIsFoundByName()
    {
        var registry = new JobRegistry([_report, _nightly], JobOptionsMonitor.Defaults);

        registry.Find("nightly").Should().BeSameAs(_nightly);
        registry.Definitions.Should().Equal(_report, _nightly);
    }

    [Fact]
    public void AnUnknownNameYieldsNull()
    {
        new JobRegistry([_report], JobOptionsMonitor.Defaults).Find("missing").Should().BeNull();
    }

    [Fact]
    public void ARegistrationIsFoundByItsImplementingType()
    {
        var registry = new JobRegistry([_report, _nightly], JobOptionsMonitor.Defaults);

        registry.FindByType(typeof(FailingJob)).Should().BeSameAs(_nightly);
        registry.FindByType(typeof(SelfSchedulingJob)).Should().BeNull();
    }

    [Fact]
    public void TwoJobsUnderOneNameFailAtStartupRatherThanAtThreeInTheMorning()
    {
        var duplicate = new JobDefinition("report", typeof(FailingJob), null, JobOverlapPolicy.Forbid);

        var act = () => new JobRegistry([_report, duplicate], JobOptionsMonitor.Defaults);

        act.Should().Throw<InvalidOperationException>().WithMessage("*must be unique*");
    }

    [Fact]
    public void AnExplicitScheduleMakesARegistrationRecurring()
    {
        _nightly.IsRecurring.Should().BeTrue();
        _report.IsRecurring.Should().BeFalse();
    }

    [Fact]
    public void AJobTypeDeclaringItsOwnScheduleIsRecurringWithoutAnExplicitOne()
    {
        var registration = new JobDefinition("self", typeof(SelfSchedulingJob), null, JobOverlapPolicy.Forbid);

        registration.IsRecurring.Should().BeTrue();
    }

    [Fact]
    public void TheNullGuardsRejectMissingArguments()
    {
        ((Action)(() => new JobRegistry(null!, JobOptionsMonitor.Defaults))).Should()
            .Throw<ArgumentNullException>().WithParameterName("definitions");
        ((Action)(() => new JobRegistry([_report], null!))).Should()
            .Throw<ArgumentNullException>().WithParameterName("options");

        var registry = new JobRegistry([_report], JobOptionsMonitor.Defaults);

        registry.Invoking(r => r.Find(null!)).Should()
            .Throw<ArgumentNullException>().WithParameterName("name");
        registry.Invoking(r => r.FindByType(null!)).Should()
            .Throw<ArgumentNullException>().WithParameterName("jobType");
        registry.Invoking(r => r.IsEnabled(null!)).Should()
            .Throw<ArgumentNullException>().WithParameterName("name");
    }

    [Fact]
    public void AJobIsEnabledUnlessItsOwnOptionsSayOtherwise()
    {
        var options = JobOptionsMonitor.Defaults.For("nightly", o => o.Enabled = false);
        var registry = new JobRegistry([_report, _nightly], options);

        registry.IsEnabled("report").Should().BeTrue();
        registry.IsEnabled("nightly").Should().BeFalse();
    }

    [Fact]
    public void AnUnregisteredNameIsNotEnabled()
    {
        new JobRegistry([_report], JobOptionsMonitor.Defaults).IsEnabled("never-registered").Should().BeFalse();
    }
}
