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
/// A minimal DbContext used by TransactionBehavior tests to provide a real EF Core instance.
/// </summary>
public class TransactionTestDbContext : DbContext
{
    public TransactionTestDbContext(DbContextOptions<TransactionTestDbContext> options) : base(options) { }
}
