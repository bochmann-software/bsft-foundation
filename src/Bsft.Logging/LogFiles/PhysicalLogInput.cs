namespace Bsft.Logging.LogFiles;

internal sealed record PhysicalLogInput(long Offset, string Text, int ByteCount, bool IsTruncated);
