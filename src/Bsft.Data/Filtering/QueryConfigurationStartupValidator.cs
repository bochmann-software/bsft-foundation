using Bsft.Core;

namespace Bsft.Data.Filtering;

/// <summary>
/// Fails application startup when a query configuration sorts by default on a field it never
/// declared as sortable.
/// </summary>
/// <remarks>
/// The default sort is the order every request that states none of its own receives, so a
/// configuration that names an undeclared field there does not fail on an exotic request: it fails
/// on the most ordinary one, as a 500, in production. The mistake is fully knowable while the
/// configuration is being compiled, which makes it a deployment error, and a deployment error must
/// be loud at startup.
/// <para>
/// Refusing it inside <see cref="QueryConfigurationBuilder{TEntity, TDto}"/> instead was rejected twice
/// over. The builder cannot know it: <c>DefaultSort</c> may legitimately precede the
/// <c>Field</c> declarations it refers to, so only the finished configuration can be judged. And
/// throwing from the compiled configuration's constructor would surface as a dependency-injection
/// activation error while the container is being built, which is precisely the unreadable failure
/// mode that the search provider's own registration was changed to avoid.
/// </para>
/// </remarks>
public sealed class QueryConfigurationStartupValidator : IStartupValidator
{
    private readonly IEnumerable<IQueryConfigurationSource> _sources;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryConfigurationStartupValidator"/> class.
    /// </summary>
    /// <param name="sources">The compiled query configurations to check.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources"/> is null.</exception>
    public QueryConfigurationStartupValidator(IEnumerable<IQueryConfigurationSource> sources)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    }

    /// <inheritdoc />
    public Task<StartupValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var sources = _sources.ToList();
        if (sources.Count == 0)
            return Task.FromResult(StartupValidationResult.Success("No query configurations registered."));

        var errors = sources
            .Where(s => s.DefaultSortField is { } field && !s.IsSortable(field))
            .Select(DescribeUnsortableDefault)
            .ToArray();

        return Task.FromResult(errors.Length == 0
            ? StartupValidationResult.Success($"{sources.Count} query configuration(s) sort by a declared field.")
            : StartupValidationResult.Failure(errors));
    }

    private static string DescribeUnsortableDefault(IQueryConfigurationSource source) =>
        $"{source.ConfigurationName} sorts by default on '{source.DefaultSortField}', but that field is not declared sortable, so every request that states no sort of its own fails. "
        + $"Declare it with Field(d => d.{source.DefaultSortField}).AllowSort(), or sort by default on a field that already allows sorting.";
}
