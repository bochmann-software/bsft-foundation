using Bsft.Tests.Shared;
using FluentAssertions;
using Xunit;

namespace Bsft.Hosting.BackgroundWork.Tests;

/// <summary>
/// This package exists so a console tool can run the background-work pipelines without the web stack, and
/// that is a contract rather than a coincidence.
/// </summary>
/// <remarks>
/// <c>Bsft.Hosting</c> pulls in ASP.NET Core, Swashbuckle, API versioning and the OpenAPI stack for the
/// web host it configures. None of that may reach this package's closure again, whichever dependency is
/// added next, so both halves are pinned: what the assembly references, and what a restore resolved.
/// </remarks>
public sealed class DependencyClosureTests
{
    private static readonly string[] WebStackPrefixes =
    [
        "Microsoft.AspNetCore",
        "Swashbuckle",
        "Asp.Versioning",
        "Microsoft.OpenApi",
    ];

    [Fact]
    public void Assembly_ReferencesNoWebStackAssembly() =>
        typeof(BackgroundWorkDefaults).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Should()
            .NotContain(name => WebStackPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));

    [Fact]
    public void Assembly_ReferencesNoHostingPackage() =>
        typeof(BackgroundWorkDefaults).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Should()
            .NotContain("Bsft.Hosting");

    [Fact]
    public void RestoredGraph_ResolvesNoWebStackLibrary() =>
        DependencyClosure.DependencyNames(DependencyClosure.RestoredGraph("Bsft.Hosting.BackgroundWork"))
            .Should()
            .NotContain(name => WebStackPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));

    [Fact]
    public void RestoredGraph_ReferencesNoAspNetCoreSharedFramework() =>
        DependencyClosure.FrameworkReferences(DependencyClosure.RestoredGraph("Bsft.Hosting.BackgroundWork"))
            .Should()
            .NotContain("Microsoft.AspNetCore.App")
            .And.OnlyContain(name => name == "Microsoft.NETCore.App");

    [Fact]
    public void RestoredGraph_TakesOnlyTheFoundationInstrumentationPackageFromBsft() =>
        DependencyClosure.DependencyNames(DependencyClosure.RestoredGraph("Bsft.Hosting.BackgroundWork"))
            .Where(name => name.StartsWith("Bsft.", StringComparison.Ordinal))
            .Should()
            .BeEquivalentTo(["Bsft.Instrumentation"]);
}
