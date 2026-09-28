using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Filtering;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Tests.Filtering;

/// <summary>An entity carrying both key conventions.</summary>
public sealed class BothKeyConventions
{
    public int Id { get; set; }

    public int BothKeyConventionsId { get; set; }
}
