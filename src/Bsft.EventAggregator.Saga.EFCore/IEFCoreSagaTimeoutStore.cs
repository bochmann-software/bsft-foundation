using Bsft.EventAggregator.Saga.Timeouts;

namespace Bsft.EventAggregator.Saga.EFCore;

/// <summary>
/// The EF Core-backed saga timeout store. Beyond <see cref="ISagaTimeoutStore"/> it can take part in
/// a saga persistence window, which is what allows a step's reminders to be committed in the same
/// transaction as its state advance.
/// </summary>
public interface IEFCoreSagaTimeoutStore : ISagaTimeoutStore
{
    /// <summary>
    /// Gets a value indicating whether writes are currently being staged into an open persistence
    /// window rather than committed on their own. Reports the mode the next write would take, which
    /// is what makes the atomic path observable from the outside.
    /// </summary>
    bool IsStagingIntoWindow { get; }
}
