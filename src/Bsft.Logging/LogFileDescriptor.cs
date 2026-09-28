namespace Bsft.Logging;

internal sealed record LogFileDescriptor(string Path, DateOnly Date, int RollIndex);
