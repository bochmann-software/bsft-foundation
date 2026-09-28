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

/// <summary>
/// A second minimal DbContext, so the multi-context tests can register two competing contexts and
/// exercise the ambiguity check and the explicit-base disambiguation.
/// </summary>
public class SecondTransactionTestDbContext : DbContext
{
    public SecondTransactionTestDbContext(DbContextOptions<SecondTransactionTestDbContext> options) : base(options) { }
}
