using System.Text.Json;
using FluentAssertions;

namespace Bsft.Tests.Shared;

/// <summary>
/// Reads what a restore actually produced for a package under <c>src</c>, for the tests that assert a
/// package does not carry a dependency edge it must not have.
/// </summary>
/// <remarks>
/// Linked into each test project that needs it rather than shipped from a package: it walks the
/// repository and reads build output, which is a fact about this checkout and nothing a consumer of
/// the framework would ever ask. Three packages assert the same "not the mediator" invariant about
/// themselves, so the reading of the graph lives here and only the assertion lives with each package.
/// </remarks>
internal static class DependencyClosure
{
    /// <summary>The restored dependency graph, which is the artefact that actually decides this.</summary>
    /// <remarks>
    /// Parsed rather than read as text, because the file also records things that say nothing about the
    /// closure — which reference packs this machine's SDK had to download, for one, which differs from
    /// machine to machine.
    /// </remarks>
    /// <param name="project">The project directory name under <c>src</c>.</param>
    /// <returns>The parsed contents of the project's <c>project.assets.json</c>.</returns>
    internal static JsonElement RestoredGraph(string project)
    {
        var path = Path.Combine(RepositoryRoot(), "src", project, "obj", "project.assets.json");

        File.Exists(path).Should().BeTrue($"'{path}' is written by restore and is what this test reads");

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement.Clone();
    }

    /// <summary>The names of every library in the restored closure, direct and transitive alike.</summary>
    /// <param name="graph">The parsed assets file.</param>
    /// <returns>The library names without their version suffix.</returns>
    /// <remarks>
    /// De-duplicated, because a closure that resolves one package id at two versions holds it twice
    /// under two <c>name/version</c> keys. A caller asserting equivalence against the set it documents
    /// would then fail over a version conflict, which is a different problem with a different fix.
    /// </remarks>
    internal static IEnumerable<string> DependencyNames(JsonElement graph)
        => graph.TryGetProperty("libraries", out var libraries) && libraries.ValueKind is JsonValueKind.Object
            ? libraries.EnumerateObject().Select(library => library.Name.Split('/')[0]).Distinct(StringComparer.Ordinal)
            : [];

    /// <summary>The shared frameworks the restored closure pulls in, such as <c>Microsoft.AspNetCore.App</c>.</summary>
    /// <param name="graph">The parsed assets file.</param>
    /// <returns>The framework reference names.</returns>
    /// <remarks>
    /// Its own question rather than part of <see cref="DependencyNames"/>: a shared framework is not a
    /// library and never appears among them, so a contracts package that imposes a web host on every
    /// consumer passes a library-only assertion untouched. The names live under a
    /// <c>frameworkReferences</c> section that the assets file repeats per target, so the search is
    /// recursive rather than a lookup at a fixed path.
    /// </remarks>
    internal static IEnumerable<string> FrameworkReferences(JsonElement graph)
        => Sections(graph, "frameworkReferences").SelectMany(Names);

    private static IEnumerable<JsonElement> Sections(JsonElement element, string name)
    {
        if (element.ValueKind is JsonValueKind.Array)
            return element.EnumerateArray().SelectMany(item => Sections(item, name));

        if (element.ValueKind is not JsonValueKind.Object)
            return [];

        return element.EnumerateObject().SelectMany(property => SectionsOf(property, name));
    }

    private static IEnumerable<JsonElement> SectionsOf(JsonProperty property, string name)
    {
        if (property.NameEquals(name))
            yield return property.Value;

        foreach (var nested in Sections(property.Value, name))
        {
            yield return nested;
        }
    }

    private static IEnumerable<string> Names(JsonElement section)
        => section.ValueKind switch
        {
            JsonValueKind.Object => section.EnumerateObject().Select(entry => entry.Name),
            JsonValueKind.Array => section.EnumerateArray()
                .Where(entry => entry.ValueKind is JsonValueKind.String)
                .Select(entry => entry.GetString()!),
            _ => [],
        };

    /// <summary>The repository root, found by walking up to the solution file.</summary>
    /// <returns>The absolute path of the repository root.</returns>
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
