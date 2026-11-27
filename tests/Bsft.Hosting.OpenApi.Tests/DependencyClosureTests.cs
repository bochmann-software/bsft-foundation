using Bsft.Hosting.OpenApi.DependencyInjection;
using Bsft.Tests.Shared;
using FluentAssertions;

namespace Bsft.Hosting.OpenApi.Tests;

/// <summary>
/// This package is where the OpenAPI and API-versioning stack enters a host, and it composes the core host
/// defaults rather than being composed by them.
/// </summary>
/// <remarks>
/// The other half of <c>Bsft.Hosting.Tests.DependencyClosureTests</c>: the arrow points from here to
/// <c>Bsft.Hosting</c>, so <c>AddBsftDefaults</c> can keep composing Swagger and versioning while the core
/// package stays free of both.
/// </remarks>
public sealed class DependencyClosureTests
{
    private const string Project = "Bsft.Hosting.OpenApi";

    [Fact]
    public void RestoredGraph_ResolvesSwashbuckleAndApiVersioning()
    {
        var dependencies = DependencyClosure.DependencyNames(DependencyClosure.RestoredGraph(Project)).ToList();

        dependencies.Should().Contain("Swashbuckle.AspNetCore");
        dependencies.Should().Contain("Asp.Versioning.Mvc.ApiExplorer");
    }

    [Fact]
    public void RestoredGraph_TakesTheCoreHostingPackage() =>
        DependencyClosure.DependencyNames(DependencyClosure.RestoredGraph(Project))
            .Should()
            .Contain("Bsft.Hosting");

    [Fact]
    public void RestoredGraph_ReferencesTheAspNetCoreSharedFramework() =>
        DependencyClosure.FrameworkReferences(DependencyClosure.RestoredGraph(Project))
            .Should()
            .Contain("Microsoft.AspNetCore.App");

    [Fact]
    public void Assembly_ReferencesTheCoreHostingAssembly() =>
        typeof(HostingServiceCollectionExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Should()
            .Contain("Bsft.Hosting");
}
