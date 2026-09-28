namespace Bsft.EventAggregator.Saga.EFCore;

/// <summary>
/// Marker interface for the EF Core-backed saga store implementation.
/// Extends <see cref="ISagaStore"/> with EF Core-specific identity.
/// </summary>
public interface IEFCoreSagaStore : ISagaStore
{
}
