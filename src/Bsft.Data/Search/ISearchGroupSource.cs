
namespace Bsft.Data.Search;

/// <summary>
/// Exposes the search groups a query configuration declares, so startup validation can check them
/// without reflecting over generic types.
/// </summary>
public interface ISearchGroupSource
{
    /// <summary>Gets the declared search groups.</summary>
    IReadOnlyList<SearchGroupConfiguration> Groups { get; }
}
