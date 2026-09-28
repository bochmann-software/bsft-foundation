using Xunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Bsft.Calendar.DependencyInjection;

namespace Bsft.Calendar.Tests;

public class BsftCalendarOptionsValidatorTests
{
    [Fact]
    public void Validate_DefaultOptions_Succeeds()
    {
        var validation = new BsftCalendarOptionsValidator();

        validation.Validate(name: null, new BsftCalendarOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithoutADefaultCalendar_FailsNamingTheMember()
    {
        var validation = new BsftCalendarOptionsValidator();

        var result = validation.Validate(name: null, new BsftCalendarOptions { Default = null! });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(BsftCalendarOptions.Default));
    }

    [Fact]
    public void Validate_NullOptions_ThrowsArgumentNullException()
    {
        var validation = new BsftCalendarOptionsValidator();

        var act = () => validation.Validate(name: null, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void AddBsftCalendar_WithoutADefaultCalendar_FailsWhenTheOptionsAreRead()
    {
        var services = new ServiceCollection();
        services.AddBsftCalendar(options => options.Default = null!);

        var act = () => services.BuildServiceProvider().GetRequiredService<IOptions<BsftCalendarOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Default*");
    }

    [Fact]
    public void AddBsftCalendar_RegistersTheValidatorOnce()
    {
        var services = new ServiceCollection();
        services.AddBsftCalendar();
        services.AddBsftCalendar();

        services.Count(sd => sd.ImplementationType == typeof(BsftCalendarOptionsValidator)).Should().Be(1);
    }

    [Fact]
    public void AddBsftCalendar_NeverRegistersTheOptionsInstanceItself()
    {
        var services = new ServiceCollection();
        services.AddBsftCalendar();

        services.Should().NotContain(sd => sd.ServiceType == typeof(BsftCalendarOptions));
    }
}
