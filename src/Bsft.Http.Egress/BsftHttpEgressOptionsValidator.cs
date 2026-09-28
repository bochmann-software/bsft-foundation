using System.Net;
using Microsoft.Extensions.Options;

namespace Bsft.Http.Egress;

/// <summary>
/// Checks a <see cref="BsftHttpEgressOptions"/> and fails the host before the first request goes out.
/// </summary>
/// <remarks>
/// The allow list is the part worth checking. It is the one way an internal address becomes reachable,
/// and an entry that is not a bare host — a whole URL pasted into the list, a name with a port on it —
/// matches nothing at all, so it reads as a hole that was opened while it is in fact a line that never
/// fires. Failing the host is the only way that is noticed.
/// </remarks>
internal sealed class BsftHttpEgressOptionsValidator : IValidateOptions<BsftHttpEgressOptions>
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public ValidateOptionsResult Validate(string? name, BsftHttpEgressOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var failures = new List<string>();

        foreach (var host in options.AllowedHosts)
            Check(failures, host);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Checks one allow-list entry.</summary>
    /// <param name="failures">The complaints collected so far.</param>
    /// <param name="host">The entry.</param>
    private static void Check(List<string> failures, string host)
    {
        var member = nameof(BsftHttpEgressOptions.AllowedHosts);

        if (string.IsNullOrWhiteSpace(host))
        {
            failures.Add($"{member} must not contain a blank entry.");

            return;
        }

        // A colon means a scheme or a port in a name and means nothing of the sort in an IPv6 literal,
        // which is full of them. Asking whether the entry parses as an address first is what keeps the
        // check from refusing every IPv6 host an operator could legitimately allow - and refusing it at
        // startup, because the options are validated on start.
        if (IPAddress.TryParse(host, out _))
            return;

        if (host.Contains('/', StringComparison.Ordinal) || host.Contains(':', StringComparison.Ordinal))
            failures.Add($"{member} entries are bare host names, so '{host}' carries a scheme, a port or a path.");
    }
}
