using Bsft.Data.Contracts.Search;

namespace Bsft.Data;

/// <summary>
/// A named set of indexed columns that are searched together, with per-field weighting.
/// </summary>
/// <param name="Name">The group name used by a search request.</param>
/// <param name="EntityType">The entity the group belongs to.</param>
/// <param name="Fields">The indexed columns of the group.</param>
/// <param name="DefaultMode">The mode used when the request does not specify one.</param>
/// <param name="Language">The stemming language of the index, as a two-letter code such as <c>de</c>.</param>
/// <param name="IsUnscoped">
/// Whether the group deliberately searches without a tenant or organisational boundary. It has to be
/// stated explicitly, because the difference between "this data is genuinely public" and "somebody
/// forgot the scoping" is invisible in the code otherwise. Startup validation accepts it only on an
/// entity carrying neither scoping marker, so the flag describes data that has no boundary — it is
/// never a switch that removes one.
/// </param>
public sealed record SearchGroupConfiguration(
    string Name,
    Type EntityType,
    IReadOnlyList<SearchField> Fields,
    SearchMode DefaultMode,
    string Language,
    bool IsUnscoped = false);
