using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace Bsft.Correlation.Http.Tests;

/// <summary>
/// The dependency edges <c>Bsft.Correlation.Http</c> must not have. None of them fails as a compiler
/// error: a <c>FrameworkReference</c> to <c>Microsoft.AspNetCore.App</c> here, or a reference to
/// <c>Bsft.Correlation</c> that brings one, compiles perfectly and quietly makes the package
/// unreferenceable from the Blazor WebAssembly client it exists for.
/// </summary>
public sealed class DependencyClosureTests
{
    private const string AspNetCoreSharedFramework = "Microsoft.AspNetCore.App";

    private const string OutboundPackage = "Bsft.Correlation.Http";

    /// <summary>The whole closure, written out rather than derived.</summary>
    /// <remarks>
    /// The contract package, and the DI abstractions the entry point is written against — which
    /// docs/package-conventions.md puts on the client-safe list because it widens a bundle by nothing.
    /// Listing the set makes the test disagree with any addition rather than only with the additions
    /// somebody thought to forbid.
    /// </remarks>
    private static readonly string[] AllowedLibraries =
    [
        "Bsft.Correlation.Abstractions",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
    ];

    [Fact]
    public void The_outbound_package_does_not_reference_the_asp_net_core_shared_framework()
    {
        // Bsft.Correlation carries that FrameworkReference for its middleware. The outbound handler is
        // the half of correlation that runs in a client, so acquiring the server stack through it would
        // undo the reason this package was split out at all.
        FrameworkReferences(RestoredGraph()).Should().NotContain(AspNetCoreSharedFramework);
    }

    [Fact]
    public void The_outbound_package_does_not_reference_asp_net_core()
    {
        DependencyNames(RestoredGraph())
            .Should().NotContain(name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void The_outbound_package_does_not_reference_the_correlation_server_half()
    {
        // The reference runs the other way: Bsft.Correlation references this package so that
        // AddBsftCorrelation() still registers the handler. An edge back would be a cycle in intent even
        // where the build tolerates it, and would drag the middleware into every client.
        DependencyNames(RestoredGraph()).Should().NotContain("Bsft.Correlation");
    }

    [Fact]
    public void The_outbound_package_depends_on_the_contract_and_the_di_abstractions_only()
    {
        LibraryNames(RestoredGraph()).Should().BeEquivalentTo(AllowedLibraries);
    }

    [Fact]
    public void The_handler_lives_in_the_outbound_assembly()
    {
        typeof(CorrelationIdDelegatingHandler).Assembly.GetName().Name.Should().Be(OutboundPackage);
    }

    [Fact]
    public void The_outbound_assembly_references_no_asp_net_core_and_not_the_server_half()
    {
        var referenced = typeof(CorrelationIdDelegatingHandler).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToList();

        referenced.Should().NotContain("Bsft.Correlation");
        referenced.Should().NotContain(name => name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    /// <summary>The restored dependency graph, which is the artefact that actually decides this.</summary>
    /// <remarks>
    /// Parsed rather than read as text, because the file also records things that say nothing about the
    /// closure: which reference packs this machine's SDK had to download, for one, which differs from
    /// machine to machine and mentions the shared framework on any host whose SDK does not already carry
    /// the exact targeting pack. A substring search over the whole file reads that as a dependency and
    /// fails for a reason that has nothing to do with what this asserts.
    /// </remarks>
    /// <returns>The parsed contents of the project's <c>project.assets.json</c>.</returns>
    private static JsonElement RestoredGraph()
    {
        var path = Path.Combine(RepositoryRoot(), "src", OutboundPackage, "obj", "project.assets.json");

        File.Exists(path).Should().BeTrue($"'{path}' is written by restore and is what this test reads");

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement.Clone();
    }

    /// <summary>Every framework the graph references, from wherever restore recorded one.</summary>
    /// <remarks>
    /// Two shapes carry the same fact: the project's own <c>frameworkReferences</c> object, and the
    /// array a resolved library contributes under <c>targets</c>. An edge that arrives through a package
    /// shows up only in the second, so both are collected.
    /// </remarks>
    /// <param name="graph">The parsed restore graph.</param>
    /// <returns>The names of every referenced framework.</returns>
    private static IEnumerable<string> FrameworkReferences(JsonElement graph)
        => Sections(graph, "frameworkReferences").SelectMany(Names);

    /// <summary>Every package and project the graph depends on, by name.</summary>
    /// <param name="graph">The parsed restore graph.</param>
    /// <returns>The names of every resolved library and of every declared dependency.</returns>
    private static IEnumerable<string> DependencyNames(JsonElement graph)
        => LibraryNames(graph).Concat(Sections(graph, "dependencies").SelectMany(Names));

    /// <summary>The resolved libraries, with the version restore appended to each identity removed.</summary>
    /// <remarks>
    /// Each name once: the package targets more than one framework, and a library resolved at a
    /// different version for each of them is listed once per version.
    /// </remarks>
    /// <param name="graph">The parsed restore graph.</param>
    /// <returns>The name of every library in the closure, packages and project references alike.</returns>
    private static IEnumerable<string> LibraryNames(JsonElement graph)
        => graph.TryGetProperty("libraries", out var libraries) && libraries.ValueKind is JsonValueKind.Object
            ? libraries.EnumerateObject().Select(library => library.Name.Split('/')[0]).Distinct(StringComparer.Ordinal)
            : [];

    /// <summary>Finds every section of a given name, at any depth.</summary>
    /// <param name="element">The element to search.</param>
    /// <param name="name">The property name to collect the values of.</param>
    /// <returns>The value of each matching property.</returns>
    private static IEnumerable<JsonElement> Sections(JsonElement element, string name)
    {
        if (element.ValueKind is JsonValueKind.Array)
            return element.EnumerateArray().SelectMany(item => Sections(item, name));

        if (element.ValueKind is not JsonValueKind.Object)
            return [];

        return element.EnumerateObject().SelectMany(property => SectionsOf(property, name));
    }

    /// <summary>The matching value of one property, plus whatever matches below it.</summary>
    /// <param name="property">The property to inspect.</param>
    /// <param name="name">The property name to collect the values of.</param>
    /// <returns>The property's own value when its name matches, followed by every nested match.</returns>
    private static IEnumerable<JsonElement> SectionsOf(JsonProperty property, string name)
    {
        if (property.NameEquals(name))
            yield return property.Value;

        foreach (var nested in Sections(property.Value, name))
        {
            yield return nested;
        }
    }

    /// <summary>Reads the names a section lists, whichever of its two shapes it has.</summary>
    /// <param name="section">An object keyed by name, or an array of names.</param>
    /// <returns>The names the section lists.</returns>
    private static IEnumerable<string> Names(JsonElement section)
        => section.ValueKind switch
        {
            JsonValueKind.Object => section.EnumerateObject().Select(entry => entry.Name),
            JsonValueKind.Array => section.EnumerateArray()
                .Where(entry => entry.ValueKind is JsonValueKind.String)
                .Select(entry => entry.GetString()!),
            _ => [],
        };

    /// <summary>Walks up from the test binaries until the solution file marks the repository root.</summary>
    /// <returns>The repository root directory.</returns>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Bsft.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests run from inside the repository");

        return directory!.FullName;
    }
}
