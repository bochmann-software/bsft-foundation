namespace Bsft.Localization;

/// <summary>What the resolution chain matched for a key: the value and the spelling it was found under.</summary>
/// <param name="Value">The matched value.</param>
/// <param name="Key">The key as the matching row or seed entry spells it.</param>
/// <param name="Locale">The locale as the matching row or seed entry spells it.</param>
/// <param name="IsFallback">Whether the match came from the fallback locale rather than the requested one.</param>
/// <remarks>
/// The chain matches keys and locales ignoring case, so the spelling a caller asked for and the one
/// that matched can differ. A caller that caches by what it was asked needs the second to tell a
/// spelling the store actually holds from one the caller made up.
/// </remarks>
internal readonly record struct TranslationMatch(string Value, string Key, string Locale, bool IsFallback);
