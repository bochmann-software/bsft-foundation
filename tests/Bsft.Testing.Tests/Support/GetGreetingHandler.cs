using Bsft.Core;
using Bsft.Mediator;

namespace Bsft.Testing.Tests.Support;

public class GetGreetingHandler : IQueryHandler<GetGreeting, string>
{
    public Task<string> HandleAsync(GetGreeting query, CancellationToken ct = default) =>
        Task.FromResult($"hello {query.Name}");
}
