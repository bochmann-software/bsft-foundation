using Bsft.Mediator.Transactions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bsft.Mediator.Tests.Transactions;

[Transactional]
public sealed class TransactionalScanVoidCommandHandler : ICommandHandler<TransactionalScanVoidCommand>
{
    public Task HandleAsync(TransactionalScanVoidCommand command, CancellationToken ct = default) =>
        Task.CompletedTask;
}
