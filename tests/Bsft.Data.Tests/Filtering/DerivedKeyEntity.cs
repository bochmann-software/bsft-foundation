using System.Linq.Expressions;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Filtering;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Tests.Filtering;

/// <summary>An entity whose key its base type declares.</summary>
public sealed class DerivedKeyEntity : KeyDeclaringBase
{
    public string Name { get; set; } = string.Empty;
}
