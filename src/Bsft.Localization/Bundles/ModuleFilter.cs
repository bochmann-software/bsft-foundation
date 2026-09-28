using System.Globalization;
using Bsft.Http;

namespace Bsft.Localization.Bundles;

/// <summary>
/// The set of modules a caller asked a translation bundle to be narrowed to, normalised into the
/// one form that identifies it.
/// </summary>
/// <remarks>
/// <para>
/// The convention this reads is the one <c>modules.md</c> states: a module owns every key under
/// <c>modules.{Name}.</c>. Everything that is <em>not</em> under <c>modules.</c> is shared and is
/// served with every filtered view, because a key outside the module namespace belongs to the
/// application rather than to any one module and a front end needs it whichever screen it opened.
/// A key directly under <c>modules.</c> with no further segment — <c>modules.Orders</c> — names no
/// module by that convention, so it is neither shared nor owned and never appears in a filtered
/// view.
/// </para>
/// <para>
/// Normalisation is what makes the view identifiable. The names are trimmed, upper-cased
/// invariantly, de-duplicated and ordinal-sorted, so <c>?modules=A,B</c>, <c>?modules=B,A</c> and
/// <c>?modules=b,%20a,B</c> are one selection that hashes to one entity tag — a client that
/// reorders its own list still revalidates instead of re-downloading. Upper-casing is a canonical
/// form rather than a comparison choice: the resolved bundle compares keys ordinal-ignore-case, so
/// the case a caller happened to type must not reach the tag.
/// </para>
/// </remarks>
internal sealed record ModuleFilter
{
    /// <summary>The prefix every module-owned key carries.</summary>
    private const string ModuleKeyPrefix = "modules.";

    private readonly IReadOnlyList<string> _keyPrefixes;

    private ModuleFilter(IReadOnlyList<string> modules)
    {
        Modules = modules;
        _keyPrefixes = modules.Select(m => $"{ModuleKeyPrefix}{m}.").ToList();
    }

    /// <summary>Gets the selection that narrows nothing, standing for an absent filter.</summary>
    internal static ModuleFilter None { get; } = new([]);

    /// <summary>Gets the selected module names, canonical, de-duplicated and ordinal-sorted.</summary>
    internal IReadOnlyList<string> Modules { get; }

    /// <summary>Gets a value indicating whether the selection narrows nothing.</summary>
    internal bool IsEmpty => Modules.Count == 0;

    /// <summary>
    /// Builds the selection of an already validated set of canonical module names.
    /// </summary>
    /// <param name="canonicalNames">The upper-cased, de-duplicated names; <see cref="ModuleFilterResult.Parse"/> produces them.</param>
    /// <returns>The selection, its names ordinal-sorted.</returns>
    /// <remarks>
    /// Validation and normalisation live in <see cref="ModuleFilterResult.Parse"/>, the one place that
    /// can answer either outcome, so the selection does not depend on the result that carries it.
    /// </remarks>
    internal static ModuleFilter Of(IEnumerable<string> canonicalNames)
        => new(canonicalNames.OrderBy(name => name, StringComparer.Ordinal).ToList());

    /// <summary>
    /// Reduces the view this selection names to the version that identifies it.
    /// </summary>
    /// <param name="bundle">The fully resolved bundle the view is cut from.</param>
    /// <returns>The SHA-256 of the narrowed representation, as hexadecimal.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="bundle"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// The empty selection returns the bundle's own version unchanged, so a request that narrows
    /// nothing is served the tag it was served before this filter existed.
    /// </para>
    /// <para>
    /// Otherwise the version is taken over the full bundle's version and the selection rather than
    /// over the narrowed content. The full version already identifies the locale, the tenant and the
    /// content it was hashed from, and the selection determines what was kept out of it, so the pair
    /// identifies the narrowed representation exactly as a hash of that representation would — while
    /// costing a hash of a few dozen bytes instead of a pass over every translation in the locale.
    /// That is what makes a conditional request on a narrowed view cheap: the tag is known before
    /// anything is narrowed, so a 304 never builds the view it stands in for.
    /// </para>
    /// </remarks>
    internal string VersionOf(TranslationBundle bundle)
    {
        if (bundle is null)
            throw new ArgumentNullException(nameof(bundle));

        return IsEmpty ? bundle.Version : VersionSegments(bundle.Version).ToCanonicalHash();
    }

    /// <summary>
    /// Cuts the view of a resolved bundle this selection names: the selected modules' keys plus
    /// everything shared.
    /// </summary>
    /// <param name="bundle">The fully resolved bundle to narrow.</param>
    /// <returns>The keys this selection admits, with the values the fallback chain resolved them to.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="bundle"/> is null.</exception>
    /// <remarks>
    /// The empty selection hands back the bundle's own content, by reference and unfiltered.
    /// </remarks>
    internal IReadOnlyDictionary<string, string> Narrow(TranslationBundle bundle)
    {
        if (bundle is null)
            throw new ArgumentNullException(nameof(bundle));

        if (IsEmpty)
            return bundle.Translations;

        var narrowed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in bundle.Translations)
        {
            if (Contains(entry.Key))
                narrowed[entry.Key] = entry.Value;
        }

        return narrowed;
    }

    /// <summary>
    /// Streams a narrowed bundle into the fixed sequence of segments its version is taken over.
    /// </summary>
    /// <param name="bundleVersion">The version of the full bundle the view was cut from.</param>
    /// <returns>The segments, in the order they are hashed in.</returns>
    /// <remarks>
    /// The selection arrives here normalised, so <c>?modules=A,B</c> and <c>?modules=B,A</c> reach
    /// this in one form and hash alike. The list is preceded by its length, so that the segment
    /// framing it can never be read as part of it — uniform with how the full bundle's version
    /// frames its own list.
    /// </remarks>
    private IEnumerable<string?> VersionSegments(string bundleVersion)
    {
        yield return bundleVersion;
        yield return Modules.Count.ToString(CultureInfo.InvariantCulture);

        foreach (var module in Modules)
        {
            yield return module;
        }
    }

    /// <summary>
    /// Reports whether a key belongs in the narrowed view.
    /// </summary>
    /// <param name="key">The translation key.</param>
    /// <returns><see langword="true"/> when the key is shared or owned by a selected module.</returns>
    private bool Contains(string key)
        => !key.StartsWith(ModuleKeyPrefix, StringComparison.OrdinalIgnoreCase) || IsOwnedBySelection(key);

    /// <summary>
    /// Reports whether a module-namespaced key belongs to one of the selected modules.
    /// </summary>
    /// <param name="key">The translation key, known to be under the module prefix.</param>
    /// <returns><see langword="true"/> when a selected module owns it.</returns>
    private bool IsOwnedBySelection(string key)
        => _keyPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
