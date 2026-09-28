namespace Bsft.Resilience;

/// <summary>
/// What the stream factory is told about the attempt it is being invoked for. It is the opt-in seam for
/// resuming rather than restarting: a factory that can ask its source to skip the first
/// <see cref="ItemsYielded"/> items turns <see cref="PartialStreamPolicy.RestartFromStart"/> from
/// "produces duplicates" into "continues where it left off".
/// </summary>
/// <param name="AttemptNumber">
/// Zero on the first invocation and one higher on each retry, so a factory can log the attempt or widen
/// a timeout as the attempts progress.
/// </param>
/// <param name="ItemsYielded">
/// How many items the consumer has already been handed across all previous attempts. Zero on the first
/// invocation. A factory that ignores it starts over, which is what
/// <see cref="PartialStreamPolicy.RestartFromStart"/> means without the hook.
/// </param>
public readonly record struct StreamRetryContext(int AttemptNumber, long ItemsYielded);
