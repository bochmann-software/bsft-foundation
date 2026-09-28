namespace Bsft.Core.Polling;

/// <summary>
/// What a finished poll pass tells the loop to do next.
/// </summary>
/// <remarks>
/// This is the one degree of freedom the loop leaves its passes. A dispatcher that drained a full
/// batch knows more work is waiting and polls again at once, while a sweep that runs on a fixed
/// cadence always waits. Both are the same loop, and the difference is stated here rather than
/// duplicated as a second loop.
/// </remarks>
public enum PollPassOutcome
{
    /// <summary>Wait for the poll interval before the next pass. The default outcome.</summary>
    WaitForInterval = 0,

    /// <summary>Start the next pass at once, because the pass left work behind.</summary>
    PollAgain = 1,
}
