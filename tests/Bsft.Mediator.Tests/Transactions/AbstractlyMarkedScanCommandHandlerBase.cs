using Bsft.Mediator.Transactions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bsft.Mediator.Tests.Transactions;

[Transactional]
public abstract class AbstractlyMarkedScanCommandHandlerBase : ICommandHandler<AbstractlyMarkedScanCommand, string>
{
    public Task<string> HandleAsync(AbstractlyMarkedScanCommand command, CancellationToken ct = default) =>
        Task.FromResult("handled");
}
