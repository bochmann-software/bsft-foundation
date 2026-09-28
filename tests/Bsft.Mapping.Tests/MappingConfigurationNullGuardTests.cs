using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class MappingConfigurationNullGuardTests
{
    [Fact]
    public void AddProfilesFromAssembly_NullAssembly_ThrowsArgumentNullException()
    {
        // MappingConfiguration.AddProfilesFromAssembly: null assembly guard.
        var config = new MappingConfiguration();

        var act = () => config.AddProfilesFromAssembly(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("assembly");
    }
}
