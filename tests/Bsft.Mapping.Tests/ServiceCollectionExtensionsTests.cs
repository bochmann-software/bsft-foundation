using System.Reflection;
using Bsft.Core;
using Bsft.Mapping.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Bsft.Mapping.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBsftMapping_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBsftMapping(_ => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftMapping_NullConfigure_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBsftMapping(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configure");
    }

    [Fact]
    public void AddBsftMapping_RegistersMapperAndStartupValidator()
    {
        // MappingServiceCollectionExtensions.AddBsftMapping:
        // Mapper, MappingValidationService, and MappingStartupValidator are registered.
        var services = new ServiceCollection();
        services.AddBsftMapping(cfg => cfg.AddProfilesFromAssembly(typeof(ConventionProfile).Assembly));

        services.Should().Contain(sd => sd.ServiceType == typeof(IMapper));
        services.Should().Contain(sd => sd.ServiceType == typeof(IMappingValidationService));
        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IStartupValidator) &&
            sd.ImplementationType == typeof(MappingStartupValidator));
    }

    [Fact]
    public void AddBsftMapping_ScanningAssemblyWithAbstractTypes_SkipsNonConcreteTypes()
    {
        // MappingServiceCollectionExtensions.AddBsftMapping:
        // (!type.IsClass || type.IsAbstract) => continue
        // The Bsft.Mapping production assembly contains abstract MappingProfileBase and interfaces,
        // so scanning it will trigger the continue branch for those types.
        var services = new ServiceCollection();

        // Scanning the production Bsft.Mapping assembly triggers the continue branch
        // because it contains abstract MappingProfileBase and interfaces.
        // Scanning the test assembly adds concrete profiles.
        var act = () => services.AddBsftMapping(cfg =>
        {
            cfg.AddProfilesFromAssembly(typeof(MappingProfileBase).Assembly);
            cfg.AddProfilesFromAssembly(typeof(ConventionProfile).Assembly);
        });

        // Should not throw - abstract/interface types in production assembly are skipped
        act.Should().NotThrow();
        services.Should().Contain(sd => sd.ServiceType == typeof(IMapper));
    }

    [Fact]
    public void AddBsftMapping_AssemblyGetTypesThrowsReflectionTypeLoadException_UsesPartialTypes()
    {
        // MappingServiceCollectionExtensions.AddBsftMapping:
        // ReflectionTypeLoadException catch block: uses ex.Types (filtered non-null).
        // We use a mock Assembly whose GetTypes() throws ReflectionTypeLoadException.
        var mockAssembly = Substitute.For<Assembly>();
        var loadedException = new ReflectionTypeLoadException(
            [typeof(ConventionProfile)],   // partially loaded: a concrete MappingProfileBase subclass
            [new Exception("load error")]);
        mockAssembly.When(a => a.GetTypes()).Throw(loadedException);

        var services = new ServiceCollection();

        // The catch block handles the exception and uses ConventionProfile from ex.Types
        var act = () => services.AddBsftMapping(cfg => cfg.AddProfilesFromAssembly(mockAssembly));

        act.Should().NotThrow();
        services.Should().Contain(sd => sd.ServiceType == typeof(IMapper));
    }
}
