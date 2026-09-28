using Bsft.Data.Search;

namespace Bsft.Data.Sqlite.Tests.Support;

public sealed class FixedScopeProvider : ISearchScopeProvider
{
    private readonly SearchScopeState _scope;

    public FixedScopeProvider(SearchScopeState scope) => _scope = scope;

    public Task<SearchScopeState> GetScopeAsync(CancellationToken ct = default) => Task.FromResult(_scope);
}
