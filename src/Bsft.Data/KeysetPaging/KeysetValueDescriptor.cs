namespace Bsft.Data.KeysetPaging;

/// <summary>How one supported type travels in a keyset cursor.</summary>
/// <param name="Code">The type code the cursor carries, so a cursor of other types is refused.</param>
/// <param name="Format">Writes a value as invariant text that parses back to the same value.</param>
/// <param name="Parse">Reads the text back, or returns null when it is not a value of the type.</param>
internal sealed record KeysetValueDescriptor(byte Code, Func<object, string> Format, Func<string, object?> Parse);
