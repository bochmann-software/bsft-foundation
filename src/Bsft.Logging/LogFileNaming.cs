using System.Globalization;

namespace Bsft.Logging;

internal sealed class LogFileNaming : ILogFileNaming
{
    private const string Prefix = "app-";
    private const string DateFormat = "yyyy-MM-dd";
    private const int PrefixLength = 4;
    private const int DateLength = 10;
    private const int BaseNameLength = PrefixLength + DateLength;
    private const int RollStart = BaseNameLength + 1;

    /// <inheritdoc />
    public string AllFilesPattern => $"{Prefix}*.log";

    /// <inheritdoc />
    public string FilesForDatePattern(DateOnly date)
        => $"{Prefix}{date.ToString(DateFormat, CultureInfo.InvariantCulture)}*.log";

    /// <inheritdoc />
    public string PathFor(string directory, DateOnly date, int rollIndex)
    {
        var suffix = rollIndex == 0 ? string.Empty : $"_{rollIndex:D3}";
        return Path.Combine(
            directory,
            $"{Prefix}{date.ToString(DateFormat, CultureInfo.InvariantCulture)}{suffix}.log");
    }

    /// <inheritdoc />
    public LogFileDescriptor? DescribeOrNull(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (!name.StartsWith(Prefix, StringComparison.Ordinal)
            || name.Length < BaseNameLength
            || !DateOnly.TryParseExact(
                name.AsSpan(PrefixLength, DateLength),
                DateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
            return null;

        var roll = 0;
        if (name.Length > BaseNameLength
            && (name[BaseNameLength] != '_'
                || !int.TryParse(name.AsSpan(RollStart), CultureInfo.InvariantCulture, out roll)))
            return null;

        return new LogFileDescriptor(path, date, roll);
    }
}
