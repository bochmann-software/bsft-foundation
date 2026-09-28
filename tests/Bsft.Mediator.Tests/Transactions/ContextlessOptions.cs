using Bsft.Mediator.Transactions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests.Transactions;

/// <summary>Options that name no context type at all.</summary>
internal sealed class ContextlessOptions : DbContextOptions
{
    public override Type ContextType => null!;

    public override DbContextOptions WithExtension<TExtension>(TExtension extension) =>
        throw new NotSupportedException();
}
