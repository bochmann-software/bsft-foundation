using System.Reflection;
using System.Text.Json;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Contracts.Search;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Tests;

/// <summary>
/// The dependency edges <c>Bsft.Data.Contracts</c> must not have, and the namespace boundary that
/// keeps it distinguishable from <c>Bsft.Data</c>. Neither fails as a compiler error: a reference to
/// EF Core from the contracts package compiles perfectly and quietly puts the whole provider stack
/// into the bundle of the Blazor WebAssembly client this package exists for, and a namespace shared
/// with the implementation compiles perfectly and leaves a reader unable to tell from a
/// <c>using</c> which of the two assemblies a type came from.
/// </summary>
public sealed class DependencyClosureTests
{
    private const string AspNetCoreSharedFramework = "Microsoft.AspNetCore.App";

    private const string ContractsPackage = "Bsft.Data.Contracts";

    /// <summary>Every type that left <c>Bsft.Data</c> and must still be reachable through it.</summary>
    /// <remarks>
    /// Written out rather than derived from the contracts assembly. Deriving it would make the test
    /// agree with whatever the move happened to do; listing it makes the test disagree when the move
    /// forgets one.
    /// </remarks>
    public static TheoryData<Type> MovedTypes() =>
    [
        typeof(AndNode),
        typeof(ComparisonNode),
        typeof(FilterNode),
        typeof(FilterOperator),
        typeof(FilterOperatorJsonConverter),
        typeof(OrNode),
        typeof(PagedRequest),
        typeof(PagedResult<>),
        typeof(SortDirection),
        typeof(SortItem),
        typeof(RankedItem<>),
        typeof(SearchMode),
        typeof(SearchRequest),
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
    public void The_contracts_package_does_not_reference_the_asp_net_core_shared_framework()
    {
        // Bsft.Data pulls Microsoft.AspNetCore.Http.Abstractions in for the tenant middleware. That
        // is correct there and wrong in a package a browser client references, which is the whole
        // reason the query contracts left it.
        FrameworkReferences(RestoredGraph(ContractsPackage)).Should().NotContain(AspNetCoreSharedFramework);
    }

    [Fact]
    public void The_contracts_package_does_not_reference_ef_core_or_asp_net_core()
    {
        DependencyNames(RestoredGraph(ContractsPackage))
            .Should().NotContain(name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            .And.NotContain(name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void The_contracts_package_has_no_dependencies_at_all()
    {
        // Stated as the closure being empty rather than as a list of forbidden names, because the
        // next dependency to threaten the bundle is the one nobody thought to forbid. A deliberate
        // addition here has to change this test and say why in the same commit.
        LibraryNames(RestoredGraph(ContractsPackage)).Should().BeEmpty();
    }

    [Fact]
    public void The_contracts_assembly_references_nothing_of_the_data_layer()
    {
        var referenced = typeof(PagedRequest).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToList();

        referenced.Should().NotContain("Bsft.Data");
        referenced.Should().NotContain("Bsft.Core");
        referenced.Should().NotContain(name => name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(MovedTypes))]
    public void Every_moved_type_now_lives_in_the_contracts_assembly(Type moved)
    {
        moved.Assembly.GetName().Name.Should().Be(ContractsPackage);
    }

    [Theory]
    [MemberData(nameof(MovedTypes))]
    public void Every_moved_type_is_rooted_in_the_contracts_namespace(Type moved)
    {
        // package-conventions.md section 3: a .Contracts package uses its own assembly name as
        // namespace root, and two assemblies never share a namespace. Until 1.3.5 these types
        // declared Bsft.Data.Filtering, Bsft.Data.Pagination and Bsft.Data.Search — the same three
        // namespaces Bsft.Data itself declares — so a using said nothing about which assembly it
        // reached, and moving a type between the two was invisible in a diff.
        moved.Namespace.Should().StartWith(ContractsPackage + ".");
    }

    [Fact]
    public void The_two_assemblies_share_no_namespace()
    {
        // The theory above walks a hand-written list, which cannot notice a type added later under
        // the old root. This closes that: no namespace published by the contracts assembly may also
        // be published by the data assembly, whichever types either of them grows.
        var contracts = Namespaces(typeof(PagedRequest).Assembly);
        var data = Namespaces(typeof(QueryableExtensions).Assembly);

        contracts.Intersect(data, StringComparer.Ordinal).Should().BeEmpty(
            "a consumer reads the assembly a type comes from off its namespace");
    }

    /// <summary>The namespaces an assembly publishes.</summary>
    /// <param name="assembly">The assembly to read.</param>
    /// <returns>The distinct namespace of every exported type.</returns>
    private static IReadOnlyCollection<string> Namespaces(Assembly assembly)
        => assembly.GetExportedTypes()
            .Select(type => type.Namespace)
            .Where(name => name is not null)
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
