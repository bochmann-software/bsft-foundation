using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>A handler that throws synchronously, so it never returns a task to fault.</summary>
public class GuardingQueryHandler : IQueryHandler<GuardingQuery, string>
{
    /// <inheritdoc />
    public Task<string> HandleAsync(GuardingQuery query, CancellationToken ct = default) =>
        throw new ArgumentException("guarded");
}
