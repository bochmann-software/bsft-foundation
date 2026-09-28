using System.Buffers;
using System.Text;

namespace Bsft.Logging.LogFiles;

internal static class Utf8Boundary
{
    private const byte ContinuationMarkerMask = 0xC0;
    private const byte ContinuationMarker = 0x80;

    internal static (int Start, int Length) CompleteRange(ReadOnlySpan<byte> bytes)
    {
        var start = 0;
        while (start < bytes.Length && IsContinuation(bytes[start]))
            start++;

        var completeLength = 0;
        var remaining = bytes[start..];
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf8(remaining, out _, out var consumed);
            if (status != OperationStatus.Done)
                break;

            completeLength += consumed;
            remaining = remaining[consumed..];
        }

        return (start, completeLength);
    }

    private static bool IsContinuation(byte value)
        => (value & ContinuationMarkerMask) == ContinuationMarker;
}
