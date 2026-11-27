namespace Bsft.Scheduling;

/// <summary>
/// The machine-readable error codes the scheduler raises.
/// </summary>
public static class SchedulingErrorCodes
{
    /// <summary>
    /// A job recorded a run result whose serialized value exceeds
    /// <see cref="BsftSchedulingOptions.MaxResultBytes"/>.
    /// </summary>
    public const string ResultTooLarge = "SCHEDULING_RESULT_TOO_LARGE";
}
