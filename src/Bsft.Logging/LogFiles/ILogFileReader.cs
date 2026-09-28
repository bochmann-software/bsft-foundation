namespace Bsft.Logging.LogFiles;

/// <summary>Reads and follows the rolling files produced by <see cref="BsftFileLoggerProvider"/>.</summary>
public interface ILogFileReader
{
    /// <summary>Reads one newest-first page.</summary>
    /// <param name="query">The page request.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The requested page.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the query page size is outside the supported range from 1 through 1000.
    /// </exception>
    /// <exception cref="InvalidDataException">A discovered file cannot fit the documented 63-bit sequence layout.</exception>
    Task<LogReadPageResult> ReadAsync(LogReadRequest query, CancellationToken cancellationToken = default);

    /// <summary>Follows matching entries written after the query cursor.</summary>
    /// <param name="query">The filters and optional exclusive last-seen cursor. Page size is ignored.</param>
    /// <param name="cancellationToken">Stops polling.</param>
    /// <returns>The live entry stream.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is null.</exception>
    /// <exception cref="InvalidDataException">A discovered file cannot fit the documented 63-bit sequence layout.</exception>
    IAsyncEnumerable<LogFileEntry> TailAsync(LogReadRequest query, CancellationToken cancellationToken = default);
}
