using Bsft.Data.MultiTenancy.Sandboxes;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy.Sandboxes;

/// <summary>A cleanup handler with a name of its own, where a substitute would carry a proxy's.</summary>
internal sealed class NamedCleanup : ISandboxTenantCleanup
{
    public Task ClearAsync(string tenantId, CancellationToken ct = default) => Task.CompletedTask;
}
