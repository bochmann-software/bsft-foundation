namespace Bsft.Hosting.BackgroundWork.Pipelines;

/// <summary>What an operation raced by <see cref="IOperationRacer"/> came to.</summary>
/// <typeparam name="TValue">The value the operation produces.</typeparam>
/// <param name="Status">How the operation ended.</param>
/// <param name="Value">The value, when <paramref name="Status"/> is <see cref="RacedStatus.Completed"/>.</param>
/// <param name="Fault">The failure, when <paramref name="Status"/> is <see cref="RacedStatus.Faulted"/>.</param>
internal sealed record RacedResult<TValue>(RacedStatus Status, TValue? Value, Exception? Fault);
