using Bsft.Core;
using Bsft.Mediator.Transactions;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Reflection;
using Xunit;

namespace Bsft.Mediator.Tests.Transactions;

[Transactional]
public class TransactionalVoidCommandHandler : ICommandHandler<TransactionalVoidCommand>
{
    public Task HandleAsync(TransactionalVoidCommand command, CancellationToken ct = default) => Task.CompletedTask;
}
