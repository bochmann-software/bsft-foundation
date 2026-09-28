using Bsft.Calendar.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Calendar.Tests;

public class BusinessCalendarProviderTests
{
    [Fact]
    public void NullKey_ResolvesTheDefaultCalendar()
    {
        var provider = BuildProvider();

        provider.Get(null).Should().BeSameAs(ContinuousBusinessCalendar.Instance);
    }

    [Fact]
    public void RegisteredKey_ResolvesThatCalendar()
    {
        var berlin = new WeekdayBusinessCalendar();
        var provider = BuildProvider(o => o.AddCalendar("berlin", berlin));

        provider.Get("berlin").Should().BeSameAs(berlin);
    }

    [Fact]
    public void UnknownKey_Throws()
    {
        var provider = BuildProvider();

        var act = () => provider.Get("nope");

        act.Should().Throw<InvalidOperationException>().WithMessage("*nope*");
    }

    [Fact]
    public void DefaultCalendar_CanBeReplaced()
    {
        var weekdays = new WeekdayBusinessCalendar();
        var provider = BuildProvider(o => o.Default = weekdays);

        provider.Get(null).Should().BeSameAs(weekdays);
    }

    [Fact]
    public void AddCalendar_RejectsAnEmptyKey()
    {
        var options = new BsftCalendarOptions();

        var act = () => options.AddCalendar("  ", ContinuousBusinessCalendar.Instance);

        act.Should().Throw<ArgumentException>().WithParameterName("key");
    }

    [Fact]
    public void AddCalendar_RejectsANullCalendar()
    {
        var options = new BsftCalendarOptions();

        var act = () => options.AddCalendar("berlin", null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("calendar");
    }

    [Fact]
    public void AddCalendar_IsChainableAndExposesTheRegistrations()
    {
        var options = new BsftCalendarOptions();

        options.AddCalendar("a", ContinuousBusinessCalendar.Instance)
            .AddCalendar("b", new WeekdayBusinessCalendar());

        options.Calendars.Should().HaveCount(2).And.ContainKeys("a", "b");
    }

    [Fact]
    public void AddBsftCalendar_RejectsANullServiceCollection()
    {
        var act = () => CalendarServiceCollectionExtensions.AddBsftCalendar(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftCalendar_IsAdditiveRegardlessOfOrder()
    {
        var services = new ServiceCollection();

        // A package registering its defaults must not discard what the application registers after it.
        services.AddBsftCalendar();
        services.AddBsftCalendar(o => o.AddCalendar("berlin", new WeekdayBusinessCalendar()));
        services.AddBsftCalendar(o => o.AddCalendar("hamburg", new WeekdayBusinessCalendar()));

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IBusinessCalendarProvider>();

        provider.Get("berlin").Should().BeOfType<WeekdayBusinessCalendar>();
        provider.Get("hamburg").Should().BeOfType<WeekdayBusinessCalendar>();
        provider.Get(null).Should().BeSameAs(ContinuousBusinessCalendar.Instance);
    }

    [Fact]
    public void AddBsftCalendar_KeepsAnEarlierConfigurationWhenCalledAgainWithout()
    {
        var services = new ServiceCollection();

        services.AddBsftCalendar(o => o.AddCalendar("berlin", new WeekdayBusinessCalendar()));
        services.AddBsftCalendar();

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IBusinessCalendarProvider>().Get("berlin").Should().BeOfType<WeekdayBusinessCalendar>();
    }

    private static IBusinessCalendarProvider BuildProvider(Action<BsftCalendarOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddBsftCalendar(configure);
        return services.BuildServiceProvider().GetRequiredService<IBusinessCalendarProvider>();
    }
}
