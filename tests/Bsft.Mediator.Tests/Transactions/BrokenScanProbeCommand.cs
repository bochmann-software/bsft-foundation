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

// A dedicated command type used ONLY by the broken-assembly test. Because IsTransactionRequired
// caches its result per closed generic type, this test needs a request type whose cache entry is
// computed fresh (after the broken assembly is loaded) so the reflection scan actually runs.
public record BrokenScanProbeCommand(string Value) : ICommand<string>;
