using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Bsft.Localization.Bundles;

/// <summary>
/// The outcome of normalising a <c>?modules=</c> query parameter: either the selection it names, or
/// the reason it cannot be used.
/// </summary>
/// <param name="Filter">The accepted selection, or <see langword="null"/> when the parameter was rejected.</param>
/// <param name="Error">
/// The reason the parameter was rejected, in a form fit to answer the caller with, or
/// <see langword="null"/> when it was accepted.
/// </param>
/// <remarks>
/// <para>
/// A result rather than an exception: a malformed query parameter is an ordinary answer the
/// endpoint gives — a 400 — not an exceptional condition, and routing it through the type system
/// keeps the endpoint's two outcomes visible in its own body.
/// </para>
/// <para>
/// The normalisation lives here rather than on <see cref="ModuleFilter"/> because it is the step
/// that can end in either outcome. The dependency therefore runs one way, from the result to the
/// selection it may carry.
/// </para>
/// </remarks>
internal sealed record ModuleFilterResult(ModuleFilter? Filter, string? Error)
{
    /// <summary>Separates the module names inside the query parameter.</summary>
    private const char NameSeparator = ',';

    /// <summary>Gets a value indicating whether the parameter named a usable selection.</summary>
    [MemberNotNullWhen(true, nameof(Filter))]
    internal bool IsAccepted => Filter is not null;

    /// <summary>
    /// Normalises the <c>?modules=</c> query parameter into a selection, or reports why it cannot be.
    /// </summary>
    /// <param name="raw">The raw query parameter, which may be absent or blank.</param>
    /// <param name="options">The options carrying the bounds a caller-supplied filter is held to.</param>
    /// <returns>The accepted selection, or the rejection and its reason.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <remarks>
    /// An absent parameter and one that normalises to nothing — <c>?modules=</c>, <c>?modules=,,</c>
    /// — are both <see cref="ModuleFilter.None"/> rather than an error: the parameter says which
    /// modules to keep, and naming none of them is indistinguishable from not narrowing at all. A
    /// name that matches no module is likewise not an error; it simply contributes no keys. Answering
    /// it would make the endpoint an oracle for which modules a tenant has installed, which is not
    /// something a translation bundle should disclose, and would make a front end's request fail
    /// because one module was disabled rather than degrade to the keys it can still show.
    /// </remarks>
    internal static ModuleFilterResult Parse(string? raw, BsftLocalizationOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(raw))
            return Accepted(ModuleFilter.None);

        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in raw.Split(NameSeparator))
        {
            var name = candidate.Trim();
            if (name.Length == 0)
                continue;

            var rejection = RejectionFor(name, options);
            if (rejection is not null)
                return Rejected(rejection);

            names.Add(name.ToUpperInvariant());

            // Tested inside the loop and against a set: the caller controls how many names the
            // parameter holds, so nothing is accumulated past the ceiling that would be refused
            // anyway. Counting the set rather than the names means the bound is on the modules
            // asked for, not on how often the caller repeated one of them.
            if (names.Count > options.MaxFilterModules)
            {
                return Rejected(string.Create(
                    CultureInfo.InvariantCulture,
                    $"At most {options.MaxFilterModules} distinct modules may be requested."));
            }
        }

        return Accepted(ModuleFilter.Of(names));
    }

    /// <summary>
    /// Builds the outcome of a parameter that named a usable selection.
    /// </summary>
    /// <param name="filter">The normalised selection.</param>
    /// <returns>The accepted outcome.</returns>
    internal static ModuleFilterResult Accepted(ModuleFilter filter) => new(filter, null);

    /// <summary>
    /// Builds the outcome of a parameter that cannot be used.
    /// </summary>
    /// <param name="error">The reason, in a form fit to answer the caller with.</param>
    /// <returns>The rejected outcome.</returns>
    internal static ModuleFilterResult Rejected(string error) => new(null, error);

    /// <summary>
    /// Reports why a module name cannot be used, if it cannot.
    /// </summary>
    /// <param name="name">The trimmed, non-empty candidate.</param>
    /// <param name="options">The options carrying the bounds.</param>
    /// <returns>The reason, or <see langword="null"/> when the name is usable.</returns>
    private static string? RejectionFor(string name, BsftLocalizationOptions options)
    {
        if (name.Length > options.MaxFilterModuleNameLength)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"A module name may be at most {options.MaxFilterModuleNameLength} characters long.");
        }

        return name.All(IsNameCharacter)
            ? null
            : "A module name may contain only ASCII letters, digits, '-' and '_'.";
    }

    /// <summary>
    /// Reports whether a character may appear in a module name.
    /// </summary>
    /// <param name="character">The character to test.</param>
    /// <returns><see langword="true"/> when the character is admissible.</returns>
    /// <remarks>
    /// Deliberately narrower than what a translation key may hold. A name reaches both an entity tag
    /// and a cache key, and the segment of the convention it stands for is one dot-free name — so a
    /// dot, which would let a caller select across the convention's segments, is not admitted either.
    /// </remarks>
    private static bool IsNameCharacter(char character)
        => character is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '-'
            or '_';
}
