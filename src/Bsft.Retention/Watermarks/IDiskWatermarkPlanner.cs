namespace Bsft.Retention.Watermarks;

/// <summary>
/// The policy of a disk watermark: from one measurement, how many bytes have to go and which of the
/// oldest items free them.
/// </summary>
/// <remarks>
/// Pure: it reads no clock, no volume and no archive, so the arithmetic of the limits, the target and
/// the hysteresis is tested on its own, and the pass around it only measures, records and deletes.
/// </remarks>
internal interface IDiskWatermarkPlanner
{
    /// <summary>Works out what one sweep deletes.</summary>
    /// <param name="request">The measurement the plan is made from.</param>
    /// <param name="options">The limits and the target fill ratio.</param>
    /// <returns>The items to delete, oldest first, and what the plan makes of the watermark.</returns>
    DiskWatermarkPlanResult Plan(DiskWatermarkPlanRequest request, DiskWatermarkOptions options);
}
