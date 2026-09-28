using FluentAssertions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Bsft.Idempotency.EFCore.Tests.Support;

namespace Bsft.Idempotency.EFCore.Tests;

public sealed class IdempotencyRecordEntityConfigurationTests : IDisposable
{
    private readonly SqliteFixture _sqlite = new();
    private readonly TestIdempotencyDbContext _context;

    public IdempotencyRecordEntityConfigurationTests()
    {
        _context = _sqlite.NewContext();
    }

    public void Dispose()
    {
        _context.Dispose();
        _sqlite.Dispose();
    }

    private IEntityType Entity => _context.Model.FindEntityType(typeof(IdempotencyRecordEntity))!;

    [Fact]
    public void The_records_live_in_their_own_table_keyed_by_the_surrogate() =>
        Entity.GetTableName().Should().Be("IdempotencyRecords");

    [Fact]
    public void The_key_is_the_surrogate_id() =>
        Entity.FindPrimaryKey()!.Properties.Select(property => property.Name)
            .Should().Equal(nameof(IdempotencyRecordEntity.Id));

    [Fact]
    public void The_scope_and_the_key_are_unique_together_with_the_scope_leading()
    {
        // The unique index is the arbiter the whole store leans on: the lookup that precedes a
        // reservation is a read, so two concurrent requests under one key both find nothing, and only
        // this index refuses the second insert. The scope leads so every query the store makes rides
        // it, and so one tenant's keys cannot collide with another's.
        Entity.GetIndexes()
            .Should().Contain(index =>
                index.IsUnique && index.Properties.Count == 2
                && index.Properties[0].Name == nameof(IdempotencyRecordEntity.Scope)
                && index.Properties[1].Name == nameof(IdempotencyRecordEntity.Key));
    }

    [Fact]
    public void The_expiry_is_indexed_for_the_pruner()
    {
        // Without it every sweep scans the table, which is the busiest one in a protected application.
        Entity.GetIndexes()
            .Select(index => string.Join(",", index.Properties.Select(property => property.Name)))
            .Should().Contain(nameof(IdempotencyRecordEntity.ExpiresAt));
    }

    [Fact]
    public void The_expiry_index_is_not_unique() =>
        Entity.GetIndexes()
            .Single(index => index.Properties.Count == 1
                && index.Properties[0].Name == nameof(IdempotencyRecordEntity.ExpiresAt))
            .IsUnique.Should().BeFalse();

    [Theory]
    [InlineData(nameof(IdempotencyRecordEntity.Scope), IdempotencyRecordEntityConfiguration.ScopeMaxLength)]
    [InlineData(nameof(IdempotencyRecordEntity.Key), IdempotencyRecordEntityConfiguration.KeyMaxLength)]
    [InlineData(
        nameof(IdempotencyRecordEntity.Fingerprint),
        IdempotencyRecordEntityConfiguration.FingerprintMaxLength)]
    public void The_indexed_string_columns_are_required_and_bounded(string property, int maxLength)
    {
        // A length is mandatory rather than cosmetic: an unbounded string maps to nvarchar(max) on SQL
        // Server, which is rejected outright as an index key column, and the unique index is the whole
        // point of the table.
        var column = Entity.FindProperty(property)!;

        column.IsNullable.Should().BeFalse();
        column.GetMaxLength().Should().Be(maxLength);
    }

    [Fact]
    public void The_key_column_is_as_wide_as_the_longest_key_the_options_allow() =>
        // One number rather than two, so a host cannot configure a key length the shared table refuses.
        IdempotencyRecordEntityConfiguration.KeyMaxLength
            .Should().Be(IdempotencyDefaults.MaxSupportedKeyLength);

    [Fact]
    public void The_response_columns_are_optional_while_the_request_is_running()
    {
        Entity.FindProperty(nameof(IdempotencyRecordEntity.StatusCode))!.IsNullable.Should().BeTrue();
        Entity.FindProperty(nameof(IdempotencyRecordEntity.ResponseHeaders))!.IsNullable.Should().BeTrue();
        Entity.FindProperty(nameof(IdempotencyRecordEntity.ResponseBody))!.IsNullable.Should().BeTrue();
    }

    [Fact]
    public async Task The_database_refuses_a_second_row_under_one_scope_and_key()
    {
        // The model-level assertion above says the index is declared; this says the database created
        // from it actually enforces the constraint, which is what the store's race handling relies on.
        await using var first = _sqlite.NewContext();
        first.IdempotencyRecords.Add(Record());
        await first.SaveChangesAsync();

        await using var second = _sqlite.NewContext();
        second.IdempotencyRecords.Add(Record());

        var act = async () => await second.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public void A_missing_builder_is_rejected()
    {
        var act = () => new IdempotencyRecordEntityConfiguration()
            .Configure((EntityTypeBuilder<IdempotencyRecordEntity>)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    private static IdempotencyRecordEntity Record() => new()
    {
        Scope = "scope",
        Key = "key",
        Fingerprint = "fingerprint",
        ExpiresAt = new DateTime(2026, 8, 16, 9, 0, 0, DateTimeKind.Utc),
    };
}
