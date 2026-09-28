using System.Linq.Expressions;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Filtering;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Tests.Filtering;

public sealed record DerivedKeyDto(int Id, string Name);
