using Bsft.Mediator.Transactions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bsft.Mediator.Tests.Transactions;

[Transactional]
public sealed class TransactionalScanQueryHandler : IQueryHandler<TransactionalScanQuery, string>
{
    public Task<string> HandleAsync(TransactionalScanQuery query, CancellationToken ct = default) =>
        Task.FromResult("handled");
}
