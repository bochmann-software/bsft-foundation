using Bsft.Data.MultiTenancy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy;

/// <summary>A tenant context of the host's own that cannot be told another tenant.</summary>
internal sealed class FixedTenantContext : ITenantContext
{
    public string? TenantId => "fixed";

    public bool IsResolved => true;
}
