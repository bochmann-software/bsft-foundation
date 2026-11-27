using Microsoft.Extensions.Options;

namespace Bsft.Hosting.SecurityHeaders;

/// <summary>Rejects security header options that would send a malformed header or never match a path.</summary>
internal sealed class BsftSecurityHeadersOptionsValidator : IValidateOptions<BsftSecurityHeadersOptions>
{
    /// <summary>Validates one configured instance.</summary>
    /// <param name="name">The named instance, unused: this package has one.</param>
    /// <param name="options">The instance to validate.</param>
    /// <returns>Success, or a failure naming every offending member.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public ValidateOptionsResult Validate(string? name, BsftSecurityHeadersOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        Check(failures, nameof(options.ContentTypeOptions), options.ContentTypeOptions);
        Check(failures, nameof(options.FrameOptions), options.FrameOptions);
        Check(failures, nameof(options.ReferrerPolicy), options.ReferrerPolicy);
        Check(failures, nameof(options.ContentSecurityPolicy), options.ContentSecurityPolicy);
        Check(failures, nameof(options.PermissionsPolicy), options.PermissionsPolicy);

        if (!string.IsNullOrEmpty(options.FrameOptions)
            && !options.FrameOptions.Equals("DENY", StringComparison.OrdinalIgnoreCase)
            && !options.FrameOptions.Equals("SAMEORIGIN", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{nameof(options.FrameOptions)} must be DENY or SAMEORIGIN, not '{options.FrameOptions}'.");
        }

        foreach (var path in options.ContentSecurityPolicyExemptPaths)
        {
            if (string.IsNullOrEmpty(path) || path[0] != '/')
            {
                failures.Add($"{nameof(options.ContentSecurityPolicyExemptPaths)} entries must start with '/', not '{path}'.");
            }
            else if (path == "/")
            {
                failures.Add($"{nameof(options.ContentSecurityPolicyExemptPaths)} must not contain '/', which exempts every path: use {nameof(options.ContentSecurityPolicy)} = null to disable the CSP.");
            }
            else if (path.Contains("//", StringComparison.Ordinal))
            {
                failures.Add($"{nameof(options.ContentSecurityPolicyExemptPaths)} entries must not contain '//', not '{path}'.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    // A line break in a value would split the header and let the value write headers of its own.
    private static void Check(List<string> failures, string member, string? value)
    {
        if (value is not null && value.Any(char.IsControl))
        {
            failures.Add($"{member} must not contain control characters.");
        }
    }
}
