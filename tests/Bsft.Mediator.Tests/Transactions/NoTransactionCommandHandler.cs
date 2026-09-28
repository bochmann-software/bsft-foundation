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

public class NoTransactionCommandHandler : ICommandHandler<NoTransactionCommand, string>
{
    public Task<string> HandleAsync(NoTransactionCommand command, CancellationToken ct = default)
        => Task.FromResult($"NoTx: {command.Value}");
}
