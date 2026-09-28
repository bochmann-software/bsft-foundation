using Bsft.Correlation.Http;
using FluentAssertions;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Bsft.Correlation.Tests;

/// <summary>
/// The dependency edges <c>Bsft.Correlation.Abstractions</c> must not have. None of them fails as a
/// compiler error: a <c>FrameworkReference</c> to <c>Microsoft.AspNetCore.App</c> in the abstractions
/// package compiles perfectly and quietly makes it unreferenceable from the Blazor WebAssembly client
/// the package exists for.
/// </summary>
public sealed class DependencyClosureTests
{
    private const string AspNetCoreSharedFramework = "Microsoft.AspNetCore.App";

    private const string AbstractionsPackage = "Bsft.Correlation.Abstractions";

    /// <summary>The contract surface a client that references only the abstractions must find there.</summary>
    /// <remarks>
    /// Written out rather than derived from the abstractions assembly. Deriving it would make the test
    /// agree with whatever the package happens to contain; listing it makes the test disagree when a
    /// consolidation moves one of these into the implementation package by accident.
    /// </remarks>
    public static TheoryData<Type> ClientSafeTypes() =>
    [
        typeof(ICorrelationContext),
        typeof(CorrelationDefaults),
    ];

    /// <summary>The restored dependency graph, which is the artefact that actually decides this.</summary>
    /// <remarks>
    /// Parsed rather than read as text, because the file also records things that say nothing about
    /// the closure: which reference packs this machine's SDK had to download, for one, which differs
    /// from machine to machine and mentions the shared framework on any host whose SDK does not
    /// already carry the exact targeting pack. A substring search over the whole file reads that as a
    /// dependency and fails for a reason that has nothing to do with what this asserts.
    /// </remarks>
    /// <param name="project">The project directory name under <c>src</c>.</param>
    /// <returns>The parsed contents of the project's <c>project.assets.json</c>.</returns>
    private static JsonElement RestoredGraph(string project)
    {
        var path = Path.Combine(RepositoryRoot(), "src", project, "obj", "project.assets.json");

        File.Exists(path).Should().BeTrue($"'{path}' is written by restore and is what this test reads");

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement.Clone();
    }

    /// <summary>Every framework the graph references, from wherever restore recorded one.</summary>
    /// <remarks>
    /// Two shapes carry the same fact: the project's own <c>frameworkReferences</c> object, and the
    /// array a resolved library contributes under <c>targets</c>. An edge that arrives through a
    /// package shows up only in the second, so both are collected.
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
    /// <param name="graph">The parsed restore graph.</param>
    /// <returns>The name of every library in the closure, packages and project references alike.</returns>
    private static IEnumerable<string> LibraryNames(JsonElement graph)
        => graph.TryGetProperty("libraries", out var libraries) && libraries.ValueKind is JsonValueKind.Object
            ? libraries.EnumerateObject().Select(library => library.Name.Split('/')[0])
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

    [Fact]
    public void The_abstractions_package_does_not_reference_the_asp_net_core_shared_framework()
    {
        // Bsft.Correlation carries a FrameworkReference on Microsoft.AspNetCore.App for the middleware.
        // That is correct there and wrong in a package a browser client references, which is the whole
        // reason the client-safe surface left it.
        FrameworkReferences(RestoredGraph(AbstractionsPackage)).Should().NotContain(AspNetCoreSharedFramework);
    }

    [Fact]
    public void The_abstractions_package_does_not_reference_asp_net_core()
    {
        DependencyNames(RestoredGraph(AbstractionsPackage))
            .Should().NotContain(name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void The_abstractions_package_has_no_dependencies_at_all()
    {
        // Stated as the closure being empty rather than as a list of forbidden names, because the next
        // dependency to threaten the client bundle is the one nobody thought to forbid. A deliberate
        // addition here has to change this test and say why in the same commit.
        LibraryNames(RestoredGraph(AbstractionsPackage)).Should().BeEmpty();
    }

    [Fact]
    public void The_abstractions_assembly_references_no_asp_net_core()
    {
        var referenced = typeof(ICorrelationContext).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToList();

        referenced.Should().NotContain("Bsft.Correlation");
        referenced.Should().NotContain(name => name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ClientSafeTypes))]
    public void Every_client_safe_type_lives_in_the_abstractions_assembly(Type clientSafe)
    {
        clientSafe.Assembly.GetName().Name.Should().Be(AbstractionsPackage);
    }

    [Fact]
    public void The_delegating_handler_lives_in_the_outbound_package()
    {
        // It is a live implementation, which docs/package-conventions.md keeps out of an .Abstractions
        // package by name, and it is the half a client without a request pipeline still needs. So it
        // ships as Bsft.Correlation.Http rather than in either neighbour: the abstractions package
        // stays a contract, and a client attaches the header without taking the server stack.
        typeof(CorrelationIdDelegatingHandler).Assembly.GetName().Name.Should().Be("Bsft.Correlation.Http");
    }

}
