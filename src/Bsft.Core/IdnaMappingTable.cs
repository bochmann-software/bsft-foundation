namespace Bsft.Core;

/// <summary>The UTS 46 mapping table.</summary>
/// <remarks>
/// Data, not a platform call, on purpose: <see cref="System.Globalization.IdnMapping"/> asks ICU on Linux and the operating
/// system on Windows, which know different Unicode versions and make different choices at the edges, so one name would
/// canonicalize to different values on two hosts. The file is generated from the pinned Unicode version.
/// </remarks>
internal sealed class IdnaMappingTable
{
    private const string Resource = "Bsft.Core.Data.idna-mapping.txt";

    private static readonly Lazy<IdnaMappingTable> Shared = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly CodePointMap<IdnaMapping> _mapping;

    private IdnaMappingTable(CodePointMap<IdnaMapping> mapping) => _mapping = mapping;

    /// <summary>Gets the table, loaded on first use.</summary>
    public static IdnaMappingTable Instance => Shared.Value;

    /// <summary>Looks up a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <param name="mapping">The row when the table lists the code point.</param>
    /// <returns><see langword="false"/> when the code point is disallowed.</returns>
    public bool TryGetMapping(int codePoint, out IdnaMapping? mapping) => _mapping.TryGetValue(codePoint, out mapping);

    private static IdnaMappingTable Load()
        => new(new CodePointMap<IdnaMapping>(IdnaResource.ReadRanges(Resource, null).Select(ToRange).ToList()));

    private static (int, int, IdnaMapping) ToRange((int First, int Last, string Text) range)
    {
        var parts = range.Text.Split(' ', 2);
        var status = parts[0] switch
        {
            "V" => IdnaStatus.Valid,
            "M" => IdnaStatus.Mapped,
            "D" => IdnaStatus.Deviation,
            _ => IdnaStatus.Ignored,
        };

        return (range.First, range.Last, new IdnaMapping(status, parts.Length > 1 ? IdnaResource.ParseSequence(parts[1]) : string.Empty));
    }
}
