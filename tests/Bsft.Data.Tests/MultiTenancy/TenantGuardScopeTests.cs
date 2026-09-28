using Bsft.Data.MultiTenancy;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy;

/// <summary>The declaration itself: what it covers, how long it lasts and what it is scoped to.</summary>
public class TenantGuardScopeTests
{
    [Fact]
    public void AllowTenantUnchangedWrites_WhenTheContextIsNull_ThrowsArgumentNullException()
    {
        var act = () => TenantGuardScope.AllowTenantUnchangedWrites(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsTenantUnchangedWriteAllowed_WhenNothingWasDeclared_IsFalse()
    {
        using var context = CreateContext();

        TenantGuardScope.IsTenantUnchangedWriteAllowed(context).Should().BeFalse();
    }

    [Fact]
    public void IsTenantUnchangedWriteAllowed_WhileADeclarationIsOpen_IsTrue()
    {
        using var context = CreateContext();

        using (context.AllowTenantUnchangedWrites())
            TenantGuardScope.IsTenantUnchangedWriteAllowed(context).Should().BeTrue();
    }

    [Fact]
    public void IsTenantUnchangedWriteAllowed_AfterTheDeclarationIsDisposed_IsFalseAgain()
    {
        using var context = CreateContext();

        using (context.AllowTenantUnchangedWrites())
        {
            // The declaration ends with the block, so a later save on the same context is guarded
            // again rather than staying open for the rest of the context's life.
        }

        TenantGuardScope.IsTenantUnchangedWriteAllowed(context).Should().BeFalse();
    }

    [Fact]
    public void IsTenantUnchangedWriteAllowed_WhileAnOuterDeclarationIsStillOpen_StaysTrue()
    {
        using var context = CreateContext();

        using var outer = context.AllowTenantUnchangedWrites();
        var inner = context.AllowTenantUnchangedWrites();

        inner.Dispose();

        TenantGuardScope.IsTenantUnchangedWriteAllowed(context).Should().BeTrue();
    }

    [Fact]
    public void IsTenantUnchangedWriteAllowed_WhenOneHandleIsDisposedTwice_DoesNotCloseAnotherDeclaration()
    {
        using var context = CreateContext();

        using var outer = context.AllowTenantUnchangedWrites();
        var inner = context.AllowTenantUnchangedWrites();

        inner.Dispose();
        inner.Dispose();

        TenantGuardScope.IsTenantUnchangedWriteAllowed(context).Should().BeTrue();
    }

    [Fact]
    public void IsTenantUnchangedWriteAllowed_ForAnotherContext_IsFalse()
    {
        using var declared = CreateContext();
        using var other = CreateContext();

        using var handle = declared.AllowTenantUnchangedWrites();

        TenantGuardScope.IsTenantUnchangedWriteAllowed(other).Should().BeFalse();
    }

    private static TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
