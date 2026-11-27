using Bsft.Core;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.KeysetPaging;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Data.Tests.KeysetPaging;

/// <summary>
/// Keyset paging against a real EF Core provider. Each case runs on SQLite (relational, translated to SQL)
/// and on the in-memory provider (evaluated by LINQ), the two the data tests already use.
/// </summary>
public abstract class KeysetPageReaderProviderTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 11, 12, 8, 0, 0, DateTimeKind.Unspecified);

    private static readonly KeysetOrder<MeterReading, DateTime, long> ByRecordedAt =
        new("readings.by-recorded", r => r.RecordedAt, r => r.Id);

    private static readonly KeysetOrder<MeterReading, DateTime, long> ByRecordedAtDescending =
        new("readings.by-recorded-desc", r => r.RecordedAt, r => r.Id, SortDirection.Desc);

    private static readonly KeysetOrder<MeterReading, string, Guid> ByMeter =
        new("readings.by-meter", r => r.Meter, r => r.Serial);

    private readonly KeysetPageReader _reader = new(new KeysetCursorSerializer(), new KeysetQueryComposer(), Options.Create(new BsftDataOptions()));

    private readonly MeterReadingContext _context;

    protected KeysetPageReaderProviderTests()
    {
        _context = CreateContext();
    }

    /// <summary>Creates the context on the provider under test, with its schema in place.</summary>
    protected abstract MeterReadingContext CreateContext();

    /// <summary>Releases what the provider holds besides the context.</summary>
    protected virtual void DisposeProvider()
    {
    }

    public void Dispose()
    {
        _context.Dispose();
        DisposeProvider();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(10)]
    [InlineData(11)]
    public async Task ListAsync_DuplicateSortKeys_WalksEveryRowOnceInTheListingsOrder(int pageSize)
    {
        await SeedAsync();

        var walked = await WalkAsync(ByRecordedAt, pageSize);

        walked.Should().Equal(await ExpectedAsync(ByRecordedAt));
        walked.Should().OnlyHaveUniqueItems().And.HaveCount(10);
        walked.Should().Equal(2, 5, 9, 1, 6, 10, 3, 7, 4, 8);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ListAsync_DescendingOrder_WalksEveryRowOnceInReverse(int pageSize)
    {
        await SeedAsync();

        var walked = await WalkAsync(ByRecordedAtDescending, pageSize);

        walked.Should().Equal(8, 4, 7, 3, 10, 6, 1, 9, 5, 2);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task ListAsync_StringKeyAndGuidId_WalksEveryRowOnce(int pageSize)
    {
        await SeedAsync();

        var walked = await WalkAsync(ByMeter, pageSize);

        walked.Should().Equal(await ExpectedAsync(ByMeter));
        walked.Should().Equal(3, 6, 9, 1, 4, 7, 10, 2, 5, 8);
    }

    [Fact]
    public async Task ListAsync_EmptyListing_ReturnsAnEmptyLastPage()
    {
        var page = await _reader.ListAsync(_context.Readings, ByRecordedAt, new CursorPagedRequest(PageSize: 5) { IncludeTotalCount = true });

        page.Items.Should().BeEmpty();
        page.NextCursor.Should().BeNull();
        page.PageSize.Should().Be(5);
        page.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task ListAsync_LastPageExactlyFull_HasNoNextCursor()
    {
        await SeedAsync();
        var first = await _reader.ListAsync(_context.Readings, ByRecordedAt, new CursorPagedRequest(PageSize: 5));

        var last = await _reader.ListAsync(_context.Readings, ByRecordedAt, new CursorPagedRequest(first.NextCursor, 5));

        first.NextCursor.Should().NotBeNull();
        last.Items.Should().HaveCount(5);
        last.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_LastPagePartlyFull_ReturnsTheRestWithoutNextCursor()
    {
        await SeedAsync();
        var first = await _reader.ListAsync(_context.Readings, ByRecordedAt, new CursorPagedRequest(PageSize: 7));

        var last = await _reader.ListAsync(_context.Readings, ByRecordedAt, new CursorPagedRequest(first.NextCursor, 7));

        last.Items.Select(r => r.Id).Should().Equal(7, 4, 8);
        last.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_TotalRequested_CountsTheWholeListingOnEveryPage()
    {
        await SeedAsync();
        var first = await _reader.ListAsync(_context.Readings, ByRecordedAt, new CursorPagedRequest(PageSize: 3) { IncludeTotalCount = true });

        var second = await _reader.ListAsync(_context.Readings, ByRecordedAt, new CursorPagedRequest(first.NextCursor, 3) { IncludeTotalCount = true });

        first.TotalCount.Should().Be(10);
        second.TotalCount.Should().Be(10);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public async Task ListAsync_PageSizeAboveTheMaximum_IsClampedAndReported(int pageSize)
    {
        await SeedAsync();
        var options = new BsftDataOptions();
        options.KeysetPaging.MaxPageSize = 3;
        var reader = new KeysetPageReader(new KeysetCursorSerializer(), new KeysetQueryComposer(), Options.Create(options));

        var page = await reader.ListAsync(_context.Readings, ByRecordedAt, new CursorPagedRequest(PageSize: pageSize));

        page.PageSize.Should().Be(3);
        page.Items.Select(r => r.Id).Should().Equal(2, 5, 9);
        page.NextCursor.Should().NotBeNull();
    }

    [Fact]
    public async Task ListAsync_NoTotalRequested_LeavesTheTotalOut()
    {
        await SeedAsync();

        var page = await _reader.ListAsync(_context.Readings, ByRecordedAt, new CursorPagedRequest(PageSize: 3));

        page.TotalCount.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_FilteredQuery_PagesWithinTheFilter()
    {
        await SeedAsync();
        var even = _context.Readings.Where(r => r.Id % 2 == 0);
        var first = await _reader.ListAsync(even, ByRecordedAt, new CursorPagedRequest(PageSize: 2) { IncludeTotalCount = true });

        var second = await _reader.ListAsync(even, ByRecordedAt, new CursorPagedRequest(first.NextCursor, 2));

        first.Items.Select(r => r.Id).Should().Equal(2, 6);
        first.TotalCount.Should().Be(5);
        second.Items.Select(r => r.Id).Should().Equal(10, 4);
    }

    [Fact]
    public async Task ListAsync_RowInsertedBeforeThePosition_DoesNotShiftTheNextPage()
    {
        await SeedAsync();
        var first = await _reader.ListAsync(_context.Readings, ByRecordedAt, new CursorPagedRequest(PageSize: 4));
        _context.Readings.Add(new MeterReading { Id = 11, Serial = SerialOf(11), RecordedAt = Start, Meter = "a" });
        await _context.SaveChangesAsync();

        var second = await _reader.ListAsync(_context.Readings, ByRecordedAt, new CursorPagedRequest(first.NextCursor, 4));

        second.Items.Select(r => r.Id).Should().Equal(6, 10, 3, 7);
    }

    [Fact]
    public async Task ListAsync_TamperedCursor_IsRefusedAsInvalid()
    {
        await SeedAsync();
        var first = await _reader.ListAsync(_context.Readings, ByRecordedAt, new CursorPagedRequest(PageSize: 2));
        var cursor = first.NextCursor!;
        // A character inside the payload, so every one of its bits is significant.
        var tampered = cursor[..5] + (cursor[5] == 'A' ? 'B' : 'A') + cursor[6..];

        var act = () => _reader.ListAsync(_context.Readings, ByRecordedAt, new CursorPagedRequest(tampered, 2));

        (await act.Should().ThrowAsync<ValidationException>()).Which.ErrorCode.Should().Be(KeysetPagingErrorCodes.InvalidCursor);
    }

    [Fact]
    public async Task ListAsync_CursorOfAnotherListing_IsRefusedAsInvalid()
    {
        await SeedAsync();
        var descending = await _reader.ListAsync(_context.Readings, ByRecordedAtDescending, new CursorPagedRequest(PageSize: 2));

        var act = () => _reader.ListAsync(_context.Readings, ByRecordedAt, new CursorPagedRequest(descending.NextCursor, 2));

        (await act.Should().ThrowAsync<ValidationException>()).Which.ErrorCode.Should().Be(KeysetPagingErrorCodes.InvalidCursor);
    }

    private async Task<List<long>> WalkAsync<TKey, TId>(KeysetOrder<MeterReading, TKey, TId> order, int pageSize)
    {
        var ids = new List<long>();
        string? cursor = null;

        // Bounded by the row count rather than by time: a cursor that failed to advance ends the walk
        // with too many rows instead of looping.
        for (var page = 0; page <= 11; page++)
        {
            var result = await _reader.ListAsync(_context.Readings, order, new CursorPagedRequest(cursor, pageSize));
            result.Items.Count.Should().BeLessThanOrEqualTo(pageSize);
            ids.AddRange(result.Items.Select(r => r.Id));
            cursor = result.NextCursor;
            if (cursor is null)
                break;
        }

        cursor.Should().BeNull("the walk reaches the last page");
        return ids;
    }

    /// <summary>The whole listing in one read, in the order the provider itself gives it.</summary>
    private async Task<List<long>> ExpectedAsync<TKey, TId>(KeysetOrder<MeterReading, TKey, TId> order) =>
        await new KeysetQueryComposer().Compose(_context.Readings, order, null).Select(r => r.Id).ToListAsync();

    /// <summary>
    /// Ten readings over four timestamps, three, three, two and two to a timestamp, inserted out of order,
    /// and over three meters, so both the date and the string key repeat.
    /// </summary>
    private async Task SeedAsync()
    {
        long[] ids = [4, 9, 1, 7, 2, 10, 5, 3, 8, 6];
        foreach (var id in ids)
        {
            var slot = id switch { 2 or 5 or 9 => 0, 1 or 6 or 10 => 1, 3 or 7 => 2, _ => 3 };
            _context.Readings.Add(new MeterReading
            {
                Id = id,
                Serial = SerialOf(id),
                RecordedAt = Start.AddMinutes(slot),
                Meter = ((char)('a' + (id % 3))).ToString(),
                Value = (int)id,
            });
        }

        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    /// <summary>
    /// A fixed serial per id, ordered like the id both as a <see cref="Guid"/> and as SQLite's upper-case
    /// text (<c>00000001-…</c> to <c>0000000B-…</c>), so the walk is the same on every run and provider.
    /// </summary>
    private static Guid SerialOf(long id) => new((int)id, 0, 0, new byte[8]);
}
