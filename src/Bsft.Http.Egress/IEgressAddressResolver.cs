using System.Net;

namespace Bsft.Http.Egress;

/// <summary>
/// Resolves a hostname to the addresses it currently points at.
/// </summary>
/// <remarks>
/// It exists as a seam for one reason that is not testing, although it serves that too: the same
/// resolution is performed twice — once when a destination is accepted, and again at the moment a
/// connection is opened — and an application that resolves through something other than the system
/// resolver has to be able to say so in both places at once.
/// </remarks>
public interface IEgressAddressResolver
{
    /// <summary>
    /// Resolves a host.
    /// </summary>
    /// <param name="host">The host part of a URL, which may itself be a literal address.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The addresses the host points at; empty when it points at none.</returns>
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken = default);
}
