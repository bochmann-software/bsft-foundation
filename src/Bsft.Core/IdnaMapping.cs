namespace Bsft.Core;

/// <summary>One row of the UTS 46 mapping table.</summary>
internal sealed class IdnaMapping
{
    /// <summary>Initializes a new instance of the <see cref="IdnaMapping"/> class.</summary>
    /// <param name="status">What processing does with the code point.</param>
    /// <param name="replacement">The replacement when <paramref name="status"/> is <see cref="IdnaStatus.Mapped"/>; otherwise empty.</param>
    public IdnaMapping(IdnaStatus status, string replacement)
    {
        Status = status;
        Replacement = replacement;
    }

    /// <summary>Gets what processing does with the code point.</summary>
    public IdnaStatus Status { get; }

    /// <summary>Gets the replacement of a mapped code point; empty for every other status.</summary>
    public string Replacement { get; }
}
