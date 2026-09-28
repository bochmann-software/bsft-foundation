using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Filtering;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Tests.Filtering;

/// <summary>Unit tests for the key convention itself.</summary>
public class EntityKeyTests
{
    [Fact]
    public void EntityKey_WhenTheEntityHasAnIdProperty_ResolvesIt()
    {
        var key = EntityKeyResolver.Resolve(typeof(TestEntity));

        key!.Member.Name.Should().Be("Id");
        EntityKeyResolver.FindMember(key.Selector.Body)!.Name.Should().Be("Id");
    }

    [Fact]
    public void EntityKey_WhenTheEntityNamesItsKeyAfterItself_ResolvesIt()
    {
        var key = EntityKeyResolver.Resolve(typeof(Invoice));

        key!.Member.Name.Should().Be(nameof(Invoice.InvoiceId));
    }

    [Fact]
    public void EntityKey_WhenTheEntityCarriesBothConventions_PrefersId()
    {
        EntityKeyResolver.Resolve(typeof(BothKeyConventions))!.Member.Name.Should().Be(nameof(BothKeyConventions.Id));
    }

    [Fact]
    public void EntityKey_WhenTheKeyIsANullableValue_ResolvesIt()
    {
        // Nullable<int> implements no IComparable itself; the int it wraps does, and that is what orders.
        EntityKeyResolver.Resolve(typeof(NullableKeyEntity))!.Member.Name.Should().Be(nameof(NullableKeyEntity.Id));
    }

    [Fact]
    public void EntityKey_WhenTheEntityFollowsNoKeyConvention_ResolvesNothing()
    {
        EntityKeyResolver.Resolve(typeof(Ticket)).Should().BeNull();
    }

    [Fact]
    public void EntityKey_WhenTheKeyPropertyCannotBeOrdered_ResolvesNothing()
    {
        // Ordering by a navigation property is not something a provider can translate, so such a
        // property is no key for paging purposes.
        EntityKeyResolver.Resolve(typeof(EntityWithUnorderableId)).Should().BeNull();
    }

    [Fact]
    public void EntityKey_WhenTheKeyPropertyIsAmbiguous_ResolvesNothing()
    {
        EntityKeyResolver.Resolve(typeof(DerivedEntityHidingItsKey)).Should().BeNull();
    }

    [Fact]
    public void EntityKey_WhenTheExpressionIsNoMemberAccess_ReturnsNoMember()
    {
        System.Linq.Expressions.Expression<Func<TestEntity, object?>> expression = e => e.Age + 1;

        EntityKeyResolver.FindMember(expression.Body).Should().BeNull();
    }

    [Fact]
    public void QueryFieldMap_WhenAFieldIsDeclaredTwice_KeepsTheDeclarationOrderIntact()
    {
        var builder = new QueryConfigurationBuilder<TestEntity, TestDto>();
        builder.Field(d => d.Price).AllowFilter(FilterOperator.Eq);
        builder.Field(d => d.Name).AllowSort();
        builder.Field(d => d.Price).AllowFilter(FilterOperator.Gt);

        var fieldMap = builder.BuildFieldMap();

        // Price is declared first but is not sortable, and declaring it again must not move Name.
        EntityKeyResolver.FindMember(fieldMap.GetFirstSortField()!.Body)!.Name.Should().Be("Name");
    }

    private sealed class EntityWithUnorderableId
    {
        public Ticket Id { get; set; } = new();
    }

    private class BaseEntityWithKey
    {
        public int Id { get; set; }
    }

    private sealed class DerivedEntityHidingItsKey : BaseEntityWithKey
    {
        public new long Id { get; set; }
    }
}
