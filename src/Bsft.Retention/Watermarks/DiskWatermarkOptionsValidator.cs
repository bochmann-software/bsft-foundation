using Microsoft.Extensions.Options;

namespace Bsft.Retention.Watermarks;

/// <summary>
/// Runs the rules of <see cref="DiskWatermarkOptions"/> under <c>ValidateOnStart()</c>.
/// </summary>
/// <remarks>
/// Every failure is collected and each one names the pass, for the reason
/// <see cref="BsftRetentionOptionsValidator"/> gives: these are named options, and a host may keep
/// several archives under watermarks.
/// </remarks>
internal sealed class DiskWatermarkOptionsValidator : IValidateOptions<DiskWatermarkOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DiskWatermarkOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var pass = string.IsNullOrEmpty(name) ? nameof(DiskWatermarkOptions) : name;
        var failures = new List<string>();

        // Zero would delete the whole archive on every sweep; null is how the limit is left off.
        if (options.MaxArchiveBytes is { } maxArchive && maxArchive <= 0)
        {
            failures.Add(
                $"{pass}: {nameof(DiskWatermarkOptions.MaxArchiveBytes)} must be positive when it is set, but it is {maxArchive}.");
        }

        if (options.MinAvailableBytes is { } minAvailable && minAvailable <= 0)
        {
            failures.Add(
                $"{pass}: {nameof(DiskWatermarkOptions.MinAvailableBytes)} must be positive when it is set, but it is {minAvailable}.");
        }

        // Without a path there is no volume to measure the floor on.
        if (options.MinAvailableBytes is not null && string.IsNullOrWhiteSpace(options.VolumePath))
        {
            failures.Add(
                $"{pass}: {nameof(DiskWatermarkOptions.VolumePath)} is required when {nameof(DiskWatermarkOptions.MinAvailableBytes)} is set.");
        }

        // Written so that NaN fails as well: zero would drain the whole archive, above one would drain
        // to a target past the limit that triggered it.
        if (!(options.TargetFillRatio > 0 && options.TargetFillRatio <= 1))
        {
            failures.Add(
                $"{pass}: {nameof(DiskWatermarkOptions.TargetFillRatio)} must be greater than 0 and at most 1, but it is {options.TargetFillRatio}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
