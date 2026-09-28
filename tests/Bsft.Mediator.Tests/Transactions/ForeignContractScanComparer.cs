using Bsft.Mediator.Transactions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bsft.Mediator.Tests.Transactions;

/// <summary>Marked, and generic over the request, but no handler of it.</summary>
[Transactional]
public sealed class ForeignContractScanComparer : IComparer<ForeignContractScanCommand>
{
    public int Compare(ForeignContractScanCommand? x, ForeignContractScanCommand? y) => 0;
}
