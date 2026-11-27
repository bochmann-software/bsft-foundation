namespace Bsft.Core;

/// <summary>The joining type and the virama code points the joiner rules of RFC 5892 read, from the generated Unicode data.</summary>
internal sealed class IdnaJoiningTable
{
    private const string Resource = "Bsft.Core.Data.idna-validation.txt";
    private const char NonJoining = 'U';

    private static readonly Lazy<IdnaJoiningTable> Shared = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly CodePointMap<char> _types;
    private readonly CodePointMap<bool> _viramas;

    private IdnaJoiningTable(CodePointMap<char> types, CodePointMap<bool> viramas)
    {
        _types = types;
        _viramas = viramas;
    }

    /// <summary>Gets the table, loaded on first use.</summary>
    public static IdnaJoiningTable Instance => Shared.Value;

    /// <summary>Gets the joining type of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns><c>L</c>, <c>D</c>, <c>R</c> or <c>T</c>; <c>U</c> for every other code point.</returns>
    public char GetJoiningType(int codePoint) => _types.TryGetValue(codePoint, out var value) ? value : NonJoining;

    /// <summary>Tells whether a code point is a virama (canonical combining class 9).</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns><see langword="true"/> for a virama.</returns>
    public bool IsVirama(int codePoint) => _viramas.Contains(codePoint);

    private static IdnaJoiningTable Load()
        => new(
            new CodePointMap<char>(IdnaResource.ReadRanges(Resource, "[joining]").Select(range => (range.First, range.Last, range.Text[0])).ToList()),
            new CodePointMap<bool>(IdnaResource.ReadRanges(Resource, "[virama]").Select(range => (range.First, range.Last, true)).ToList()));
}
