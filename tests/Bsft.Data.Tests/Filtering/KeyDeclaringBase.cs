using System.Linq.Expressions;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Filtering;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Tests.Filtering;

/// <summary>A base type declaring the key.</summary>
public class KeyDeclaringBase
{
    public int Id { get; set; }
}
