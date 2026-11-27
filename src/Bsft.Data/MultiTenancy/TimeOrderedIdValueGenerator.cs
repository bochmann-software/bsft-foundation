using Bsft.Core.Persistence;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Data.MultiTenancy;

/// <summary>
/// Fills the <c>Id</c> of a tenant-keyed row as the row is added, from the host's
/// <see cref="ITimeOrderedIdGenerator"/>.
/// </summary>
/// <remarks>
/// The generator comes from the context's application services, which is where
/// <c>AddBsftDataTenantKeys</c> registers it with the host's node id; services without one fall back
/// to <see cref="TimeOrderedIdGenerator.ForRandomNode"/> over their registered
/// <see cref="TimeProvider"/>. A context with neither has no clock to read the time field from and is
/// refused. An id the writer set itself is kept: EF Core only asks a value generator for a property
/// that still holds its default.
/// </remarks>
internal sealed class TimeOrderedIdValueGenerator : ValueGenerator<long>
{
    /// <inheritdoc />
    public override bool GeneratesTemporaryValues => false;

    /// <inheritdoc />
    public override long Next(EntityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var services = TenantKeyServices.FindApplicationServices(entry.Context);
        var generator = services?.GetService<ITimeOrderedIdGenerator>()
            ?? (services?.GetService<TimeProvider>() is { } clock ? TimeOrderedIdGenerator.ForRandomNode(clock) : null)
            ?? throw new InvalidOperationException(
                $"The context {entry.Context.GetType().Name} has no TimeProvider to stamp a tenant-keyed id with: "
                + "create it from a container that called AddBsftDataTenantKeys.");

        return generator.NextId();
    }
}
