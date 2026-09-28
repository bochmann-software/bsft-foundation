using Bsft.Data.Search;

namespace Bsft.Data.Tests.Support;

public sealed class FixedScopeProvider : ISearchScopeProvider
{
    public FixedScopeProvider(SearchScopeState scope) => Scope = scope;

    public SearchScopeState Scope { get; }

    public Task<SearchScopeState> GetScopeAsync(CancellationToken ct = default) => Task.FromResult(Scope);
}
