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

// Handler kept handler-only (no [Transactional]) so the assembly's startup validation still passes
// while IsTransactionRequired(BrokenScanProbeCommand) stays false.
public class BrokenScanProbeCommandHandler : ICommandHandler<BrokenScanProbeCommand, string>
{
    public Task<string> HandleAsync(BrokenScanProbeCommand command, CancellationToken ct = default)
        => Task.FromResult(command.Value);
}
