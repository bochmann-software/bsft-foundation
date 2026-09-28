using Bsft.Mediator.Transactions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bsft.Mediator.Tests.Transactions;

public sealed class UnmarkedScanCommandHandler : ICommandHandler<UnmarkedScanCommand, string>
{
    public Task<string> HandleAsync(UnmarkedScanCommand command, CancellationToken ct = default) =>
        Task.FromResult("handled");
}
