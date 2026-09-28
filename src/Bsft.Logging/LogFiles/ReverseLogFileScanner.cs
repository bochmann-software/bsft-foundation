using System.Runtime.CompilerServices;
using System.Text;

namespace Bsft.Logging.LogFiles;

internal static class ReverseLogFileScanner
{
    public static async IAsyncEnumerable<PhysicalLogInput> ReadLinesNewestAsync(
        ILogFileAccess files,
        string path,
        int maximumLineBytes,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var stream = files.OpenRead(path);
        var buffer = new byte[LogFileBufferDefaults.SizeBytes];
        var state = new ScanState(maximumLineBytes, await HasUnterminatedTailAsync(stream, cancellationToken).ConfigureAwait(false));
        var position = stream.Length;

        while (position > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min(buffer.Length, position);
            position -= count;
            stream.Position = position;
            await stream.ReadExactlyAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);

            foreach (var line in ScanChunk(buffer, count, position, state))
                yield return line;
        }

        if (state.Count > 0)
            yield return state.BuildLine(0);
    }

    private static IReadOnlyList<PhysicalLogInput> ScanChunk(
        byte[] buffer,
        int count,
        long position,
        ScanState state)
    {
        var lines = new List<PhysicalLogInput>();
        for (var index = count - 1; index >= 0; index--)
        {
            if (buffer[index] != (byte)'\n')
            {
                state.SkipTrailingTerminator = false;
                state.Add(buffer[index]);
                continue;
            }

            if (state.DiscardTrailingFragment)
            {
                state.Clear();
                state.DiscardTrailingFragment = false;
                continue;
            }

            if (state.SkipTrailingTerminator && state.Count == 0)
            {
                state.SkipTrailingTerminator = false;
                continue;
            }

            state.SkipTrailingTerminator = false;
            lines.Add(state.BuildLine(position + index + 1));
        }

        return lines;
    }

    private static async ValueTask<bool> HasUnterminatedTailAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        if (stream.Length == 0)
            return false;

        stream.Position = stream.Length - 1;
        var last = new byte[1];
        await stream.ReadExactlyAsync(last, cancellationToken).ConfigureAwait(false);
        return last[0] != (byte)'\n';
    }

    private sealed class ScanState
    {
        private readonly byte[] _bytes;
        private int _next;

        public ScanState(int maximumLineBytes, bool discardTrailingFragment)
        {
            _bytes = new byte[maximumLineBytes];
            DiscardTrailingFragment = discardTrailingFragment;
            SkipTrailingTerminator = !discardTrailingFragment;
        }

        public bool DiscardTrailingFragment { get; set; }

        public bool SkipTrailingTerminator { get; set; }

        public int Count { get; private set; }

        private bool IsTruncated { get; set; }

        public void Add(byte value)
        {
            _bytes[_next] = value;
            _next = (_next + 1) % _bytes.Length;
            if (Count < _bytes.Length)
                Count++;
            else
                IsTruncated = true;
        }

        public PhysicalLogInput BuildLine(long offset)
        {
            var bytes = new byte[Count];
            var start = Count == _bytes.Length ? _next : 0;
            for (var target = Count - 1; target >= 0; target--)
                bytes[target] = _bytes[(start + Count - 1 - target) % _bytes.Length];

            var byteCount = bytes.Length;
            if (byteCount > 0 && bytes[byteCount - 1] == (byte)'\r')
                byteCount--;
            var complete = IsTruncated
                ? Utf8Boundary.CompleteRange(bytes.AsSpan(0, byteCount))
                : (Start: 0, Length: byteCount);

            var line = new PhysicalLogInput(
                offset + complete.Start,
                Encoding.UTF8.GetString(bytes, complete.Start, complete.Length),
                complete.Length,
                IsTruncated);
            Clear();
            return line;
        }

        public void Clear()
        {
            Count = 0;
            _next = 0;
            IsTruncated = false;
        }
    }
}
