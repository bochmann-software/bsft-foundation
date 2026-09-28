using Bsft.Core;
using Bsft.Data.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Data.Search;

/// <summary>
/// Fails application startup when a declared search group cannot be served by an index, or when its
/// results would not be scoped.
/// </summary>
/// <remarks>
/// This validator is the reason the framework can promise two things: that a search never degrades
/// into a leading-wildcard <c>LIKE</c>, and that it never quietly returns rows across tenant or
/// organisational boundaries. Both failure modes are deployment errors, and a deployment error must
/// be loud at startup rather than silent in production.
/// </remarks>
public sealed class SearchStartupValidator : IStartupValidator
{
    private readonly IEnumerable<ISearchGroupSource> _sources;
    private readonly IEnumerable<ISearchProvider> _providers;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchStartupValidator"/> class.
    /// </summary>
    /// <param name="sources">All query configurations that may declare search groups.</param>
    /// <param name="providers">The registered search providers; exactly one is expected.</param>
    /// <param name="scopeFactory">Factory used to look up the scoped <see cref="ISearchScopeProvider"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public SearchStartupValidator(
        IEnumerable<ISearchGroupSource> sources,
        IEnumerable<ISearchProvider> providers,
        IServiceScopeFactory scopeFactory)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    /// <inheritdoc />
    public Task<StartupValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var groups = _sources.SelectMany(s => s.Groups).ToList();
        if (groups.Count == 0)
            return Task.FromResult(StartupValidationResult.Success("No searchable fields declared."));

        var errors = new List<string>();
        errors.AddRange(ValidateGroupNames(groups));
        errors.AddRange(ValidateScoping(groups));
        errors.AddRange(ValidateProvider(groups));

        return Task.FromResult(errors.Count == 0
            ? StartupValidationResult.Success($"{groups.Count} search group(s) are indexed and scoped.")
            : StartupValidationResult.Failure([.. errors]));
    }

    private IEnumerable<string> ValidateProvider(IReadOnlyList<SearchGroupConfiguration> groups)
    {
        var providers = _providers.ToList();

        if (providers.Count == 0)
        {
            yield return $"{groups.Count} search group(s) are declared but no {nameof(ISearchProvider)} is registered. "
                + "Add the provider package for the engine in use: Bsft.Data.SqlServer, Bsft.Data.Sqlite or Bsft.Data.Postgres.";
            yield break;
        }

        // Constructor injection hands the executor the last registration while this validator would
        // otherwise check the first. Rather than guess which one wins, refuse the ambiguity.
        if (providers.Count > 1)
        {
            yield return $"{providers.Count} {nameof(ISearchProvider)} implementations are registered "
                + $"({string.Join(", ", providers.Select(p => p.GetType().Name))}). Register exactly one.";
            yield break;
        }

        foreach (var error in providers[0].DescribeConfigurationErrors(groups))
        {
            yield return error;
        }
    }

    /// <summary>
    /// Refuses the same group name on two entities.
    /// </summary>
    /// <remarks>
    /// A provider declares its indexes by group name alone, so one <c>HasFullTextIndex("profile")</c>
    /// would certify a second entity's identically named group whose table has no index at all.
    /// Startup would pass and the first search against that table would fail in production — the
    /// precise failure this validator exists to make unreachable.
    /// </remarks>
    private static IEnumerable<string> ValidateGroupNames(IEnumerable<SearchGroupConfiguration> groups)
    {
        var ambiguous = groups
            .GroupBy(g => g.Name, StringComparer.Ordinal)
            .Select(g => new { g.Key, Entities = g.Select(x => x.EntityType.Name).Distinct(StringComparer.Ordinal).ToList() })
            .Where(g => g.Entities.Count > 1);

        foreach (var group in ambiguous)
        {
            var entities = string.Join(", ", group.Entities.OrderBy(e => e, StringComparer.Ordinal));

            yield return $"Search group name '{group.Key}' is declared on more than one entity ({entities}). "
                + "A provider declares its indexes by group name, so one declaration would certify both. Give the groups distinct names.";
        }
    }

    private List<string> ValidateScoping(IReadOnlyList<SearchGroupConfiguration> groups)
    {
        var errors = new List<string>();
        var scoped = groups.Where(g => !g.IsUnscoped).ToList();

        foreach (var group in groups.Where(g => g.IsUnscoped))
        {
            // The mirror image of the check below, and the more dangerous direction. Unscoped() is
            // the declaration for data that has no boundary at all; on an entity that carries one it
            // claims the opposite of what the runtime does, because the boundary is applied whenever
            // the resolved scope names the dimension. Left standing, that is a group whose results
            // are silently narrower than its declaration promises — or, if the flag were ever
            // honoured, one builder call that switches off tenant isolation. Refuse the declaration
            // instead of picking one of those two.
            if (SearchScopeFilter.IsScopable(group.EntityType))
            {
                var entity = group.EntityType.Name;

                errors.Add($"Search group '{group.Name}' on {entity} is declared Unscoped(), but {entity} carries a scoping marker ({nameof(ITenantEntity)} or {nameof(IOrganizationScopedEntity)}), "
                    + "so its results are bounded by the resolved scope no matter what the declaration says. Remove Unscoped(); a genuinely boundary-free group belongs on an entity that carries no scoping marker.");
            }
        }

        foreach (var group in scoped)
        {
            // An entity carrying neither marker cannot be scoped at all. Silently searching it
            // unrestricted is how personnel data crosses branches, so the declaration has to say
            // out loud that this is intended.
            if (!SearchScopeFilter.IsScopable(group.EntityType))
            {
                errors.Add($"Search group '{group.Name}' on {group.EntityType.Name} carries neither {nameof(ITenantEntity)} "
                    + $"nor {nameof(IOrganizationScopedEntity)}, so its results cannot be scoped. Implement one of them, or declare the group Unscoped().");
            }
        }

        if (scoped.Count > 0 && !SearchScopeFilter.HasScopeProvider(_scopeFactory))
        {
            errors.Add($"{scoped.Count} scoped search group(s) are declared but no {nameof(ISearchScopeProvider)} "
                + "is registered, so nothing would limit the results. Register one. Unscoped() is not the way out here: it is only accepted on an entity that carries no scoping marker.");
        }

        return errors;
    }
}
