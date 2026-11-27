using Bsft.Tests.Shared;
using FluentAssertions;

namespace Bsft.Hosting.Tests;

/// <summary>
/// The core host defaults carry no OpenAPI or API-versioning stack, and that is a contract rather than a
/// coincidence.
/// </summary>
/// <remarks>
/// A host that pins its own Swashbuckle version, to keep a generated OpenAPI document byte-identical for one,
/// must be able to take CORS, rate limiting and health checks from this package. A Swashbuckle or
/// Asp.Versioning edge here would force its version on that host again, and it would compile perfectly, so
/// both halves are pinned: what the assembly references, and what a restore resolved. Swagger, versioning
/// and <c>AddBsftDefaults</c> live in <c>Bsft.Hosting.OpenApi</c>, which references this package and not the
/// other way round.
/// </remarks>
public sealed class DependencyClosureTests
{
    private const string Project = "Bsft.Hosting";

    private static readonly string[] OpenApiStackPrefixes =
    [
        "Swashbuckle",
        "Asp.Versioning",
        "Microsoft.OpenApi",
    ];

    [Fact]
    public void Assembly_ReferencesNoOpenApiOrVersioningAssembly() =>
        typeof(HostingDefaults).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Should()
            .NotContain(name => OpenApiStackPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));

    [Fact]
    public void Assembly_ReferencesNoOpenApiPackage() =>
        typeof(HostingDefaults).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Should()
            .NotContain("Bsft.Hosting.OpenApi");

    [Fact]
    public void RestoredGraph_ResolvesNoOpenApiOrVersioningLibrary() =>
        DependencyClosure.DependencyNames(DependencyClosure.RestoredGraph(Project))
            .Should()
            .NotContain(name => OpenApiStackPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));

    [Fact]
    public void RestoredGraph_ResolvesNoOpenApiPackage() =>
        DependencyClosure.DependencyNames(DependencyClosure.RestoredGraph(Project))
            .Should()
            .NotContain("Bsft.Hosting.OpenApi");
}
