using Bsft.Data.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Tests.KeysetPaging;

/// <summary>Keyset paging evaluated by the EF Core in-memory provider.</summary>
public sealed class KeysetPageReaderInMemoryTests : KeysetPageReaderProviderTests
{
    protected override MeterReadingContext CreateContext() =>
        new(new DbContextOptionsBuilder<MeterReadingContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
