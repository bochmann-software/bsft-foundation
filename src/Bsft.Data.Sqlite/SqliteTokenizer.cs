namespace Bsft.Data.Sqlite;

/// <summary>
/// The FTS5 tokenizer an index is built with.
/// </summary>
/// <remarks>
/// The tokenizer is the whole of FTS5's language handling: it decides how text is split into tokens
/// and, for exactly one language, whether those tokens are stemmed. There is no per-language
/// dictionary the way SQL Server has one, so the choice made here is the choice made for every
/// language the index holds.
/// </remarks>
public enum SqliteTokenizer
{
    /// <summary>
    /// Unicode-aware token boundaries with diacritics folded away, so <c>müller</c> and
    /// <c>mueller</c>'s accented form index alike. No stemming in any language.
    /// </summary>
    Unicode61 = 0,

    /// <summary>
    /// <see cref="Unicode61"/> plus the Porter stemming algorithm, which is English-only. Applied to
    /// German text it does not merely fail to help, it stems by English rules.
    /// </summary>
    Porter = 1,

    /// <summary>
    /// ASCII-only token boundaries. Everything above U+007F is treated as a token character, so
    /// German text is tokenized but never folded.
    /// </summary>
    Ascii = 2,
}
