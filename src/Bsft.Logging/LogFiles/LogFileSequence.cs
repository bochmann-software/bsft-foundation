namespace Bsft.Logging.LogFiles;

internal static class LogFileSequence
{
    private const int OffsetBits = 30;
    private const int RollBits = 11;
    private const int RollShift = OffsetBits;
    private const int DayShift = OffsetBits + RollBits;

    public const int MaximumRollIndex = (1 << RollBits) - 1;
    public const long MaximumOffset = (1L << OffsetBits) - 1;

    public static long Of(LogFileDescriptor file, long offset)
    {
        if (file.RollIndex is < 0 or > MaximumRollIndex)
        {
            throw new InvalidDataException(
                $"Log file '{file.Path}' has roll index {file.RollIndex}; the supported 63-bit sequence layout permits 0 through {MaximumRollIndex}.");
        }

        if (offset is < 0 or > MaximumOffset)
        {
            throw new InvalidDataException(
                $"Log file '{file.Path}' has byte offset {offset}; the supported 63-bit sequence layout permits 0 through {MaximumOffset}.");
        }

        return ((long)file.Date.DayNumber << DayShift)
            | ((long)file.RollIndex << RollShift)
            | offset;
    }
}
