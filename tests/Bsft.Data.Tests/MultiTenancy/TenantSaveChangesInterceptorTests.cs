using Bsft.Data.MultiTenancy;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy;

public class TenantSaveChangesInterceptorTests
{
    private static (TestDbContext Context, TenantContext TenantCtx) CreateContextWithTenant(string? tenantId)
    {
        var tenantCtx = new TenantContext();
        tenantCtx.SetTenantId(tenantId);

        var interceptor = new TenantSaveChangesInterceptor(tenantCtx);
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        return (new TestDbContext(options), tenantCtx);
    }

    [Fact]
    public async Task SaveChanges_NewEntity_AutoSetsTenantId()
    {
        var (context, _) = CreateContextWithTenant("tenant-1");

        var entity = new TestEntity { Id = 1, Name = "Test" };
        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        entity.TenantId.Should().Be("tenant-1");
    }

    [Fact]
    public async Task SaveChanges_ModifiedEntity_SameTenant_Succeeds()
    {
        var (context, _) = CreateContextWithTenant("tenant-1");

        var entity = new TestEntity { Id = 1, Name = "Test" };
        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        entity.Name = "Updated";
        await context.SaveChangesAsync();

        entity.Name.Should().Be("Updated");
    }

    [Fact]
    public async Task SaveChanges_ModifiedEntity_DifferentTenant_ThrowsInvalidOperation()
    {
        var (context, tenantCtx) = CreateContextWithTenant("tenant-1");

        var entity = new TestEntity { Id = 1, Name = "Test" };
        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        tenantCtx.SetTenantId("tenant-2");
        entity.Name = "Hacked";

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cross-tenant write*");
    }

    [Fact]
    public async Task SaveChanges_MultipleEntities_AllGetTenantId()
    {
        var (context, _) = CreateContextWithTenant("tenant-1");

        var entities = new[]
        {
            new TestEntity { Id = 1, Name = "A" },
            new TestEntity { Id = 2, Name = "B" },
            new TestEntity { Id = 3, Name = "C" }
        };

        context.TestEntities.AddRange(entities);
        await context.SaveChangesAsync();

        foreach (var entity in entities)
            entity.TenantId.Should().Be("tenant-1");
    }

    [Fact]
    public async Task SaveChanges_NewEntity_NoTenantResolved_Throws()
    {
        // Regression: writing a tenant-scoped entity with no resolved tenant must be refused
        // instead of silently persisting a null TenantId.
        var (context, _) = CreateContextWithTenant(null);

        context.TestEntities.Add(new TestEntity { Id = 1, Name = "Test" });

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no tenant is resolved*");
    }

    [Fact]
    public async Task SaveChanges_ModifiedEntity_NoTenantResolved_Declared_TenantUnchanged_Succeeds()
    {
        // A background sweep writes a row back where it found it, from a scope with no ambient tenant,
        // and says so. The row stays with the tenant that already owned it, so there is nothing to
        // refuse - the entity was loaded through this context, so its original tenant is the stored one.
        var (context, tenantCtx) = CreateContextWithTenant("tenant-1");

        var entity = new TestEntity { Id = 1, Name = "Test" };
        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        tenantCtx.SetTenantId(null);
        entity.Name = "Swept";

        using var declaration = context.AllowTenantUnchangedWrites();
        var act = async () => await context.SaveChangesAsync();

        await act.Should().NotThrowAsync();
        entity.TenantId.Should().Be("tenant-1");
    }

    [Fact]
    public async Task SaveChanges_ModifiedEntity_NoTenantResolved_NotDeclared_ThrowsInvalidOperation()
    {
        // Deny by default: without the declaration, a tenant-scoped update from a tenant-less scope is
        // refused even though the entity was loaded here and its tenant is untouched.
        var (context, tenantCtx) = CreateContextWithTenant("tenant-1");

        var entity = new TestEntity { Id = 1, Name = "Test" };
        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        tenantCtx.SetTenantId(null);
        entity.Name = "Swept";

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no tenant is resolved*");
    }

    [Fact]
    public async Task SaveChanges_AttachedWithoutLoading_NoTenantResolved_NotDeclared_ThrowsInvalidOperation()
    {
        // The exploit the original-value comparison cannot see: an entity that was never loaded here
        // gets its original values seeded from its current ones, so its TenantId always looks
        // unchanged - whatever tenant the caller wrote on it. What refuses this write is the missing
        // declaration, not the comparison.
        var (context, _) = CreateContextWithTenant(null);

        context.TestEntities.Update(new TestEntity { Id = 1, Name = "Injected", TenantId = "victim" });

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no tenant is resolved*");
    }

    [Fact]
    public void Entry_AttachedWithoutLoading_SeedsTheOriginalTenantFromTheInjectedOne()
    {
        // Pins the EF Core behaviour the guard is built around, so that the reason for the declaration
        // does not have to be taken on trust: original equals current for an entity attached as
        // Modified without a prior tracked query, which is what makes the comparison vacuous alone.
        var (context, _) = CreateContextWithTenant(null);

        var entry = context.Entry(new TestEntity { Id = 1, Name = "Injected", TenantId = "victim" });
        entry.State = EntityState.Modified;

        entry.Property(nameof(TestEntity.TenantId)).OriginalValue.Should().Be("victim");
    }

    [Fact]
    public async Task SaveChanges_ModifiedEntity_NoTenantResolved_Declared_TenantMoved_ThrowsInvalidOperation()
    {
        // The declaration stops at a write that moves the row: with no ambient tenant to compare
        // against, the loaded tenant is what the entity is held to.
        var (context, tenantCtx) = CreateContextWithTenant("tenant-1");

        var entity = new TestEntity { Id = 1, Name = "Test" };
        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        tenantCtx.SetTenantId(null);
        entity.TenantId = "tenant-2";

        using var declaration = context.AllowTenantUnchangedWrites();
        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot move a tenant-scoped entity*");
    }

    [Fact]
    public async Task SaveChanges_ModifiedEntity_TenantResolved_Declared_DifferentTenant_ThrowsInvalidOperation()
    {
        // The declaration is about the tenant-less scope only. With a tenant resolved, the ambient
        // comparison is exactly the one it always was.
        var (context, tenantCtx) = CreateContextWithTenant("tenant-1");

        var entity = new TestEntity { Id = 1, Name = "Test" };
        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        tenantCtx.SetTenantId("tenant-2");
        entity.Name = "Hacked";

        using var declaration = context.AllowTenantUnchangedWrites();
        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cross-tenant write*");
    }

    [Fact]
    public async Task SaveChanges_DeletedEntity_DoesNotApplyTenantRules()
    {
        // A deleted entity is neither Added nor Modified, so the interceptor leaves it untouched.
        var (context, _) = CreateContextWithTenant("tenant-1");

        var entity = new TestEntity { Id = 1, Name = "Test" };
        context.TestEntities.Add(entity);
        await context.SaveChangesAsync();

        context.TestEntities.Remove(entity);
        var act = async () => await context.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Constructor_NullTenantContext_ThrowsArgumentNullException()
    {
        var act = () => new TenantSaveChangesInterceptor(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SaveChanges_Synchronous_NewEntity_AutoSetsTenantId()
    {
        // Exercises TenantSaveChangesInterceptor.SavingChanges lines 25-29 (synchronous overload).
        var (context, _) = CreateContextWithTenant("tenant-sync");

        var entity = new TestEntity { Id = 10, Name = "Sync" };
        context.TestEntities.Add(entity);
        context.SaveChanges(); // synchronous path -> hits SavingChanges, not SavingChangesAsync

        entity.TenantId.Should().Be("tenant-sync");
    }

    [Fact]
    public void ApplyTenantRules_NullContext_ReturnsEarly()
    {
        // Exercises TenantSaveChangesInterceptor.ApplyTenantRules line 44 (context is null -> return).
        // The private method is invoked via reflection with a null argument.
        var tenantCtx = new TenantContext();
        tenantCtx.SetTenantId("test");
        var interceptor = new TenantSaveChangesInterceptor(tenantCtx);

        var method = typeof(TenantSaveChangesInterceptor)
            .GetMethod("ApplyTenantRules", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        // Passing null DbContext -> line 44 fires (context is null -> return)
        var act = () => method.Invoke(interceptor, [null]);

        act.Should().NotThrow();
    }
}
