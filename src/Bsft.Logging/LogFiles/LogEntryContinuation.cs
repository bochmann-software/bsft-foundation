namespace Bsft.Logging.LogFiles;

internal sealed class LogEntryContinuation
{
    private const int ItemOverheadBytes = 32;

    private readonly int _maximumBytes;
    private readonly int _maximumItems;
    private int _bufferedBytes;

    public LogEntryContinuation(int maximumBytes)
    {
        _maximumBytes = maximumBytes;
        _maximumItems = Math.Max(1, maximumBytes / ItemOverheadBytes);
    }

    public Queue<PhysicalLogInput> Lines { get; } = new();

    public int MaximumEntryBytes => _maximumBytes;

    public bool WasTruncated { get; private set; }

    public void Add(PhysicalLogInput line)
    {
        Lines.Enqueue(line);
        _bufferedBytes += line.ByteCount;
        while ((_bufferedBytes > _maximumBytes || Lines.Count > _maximumItems) && Lines.Count > 0)
        {
            _bufferedBytes -= Lines.Dequeue().ByteCount;
            WasTruncated = true;
        }
    }

    public void Clear()
    {
        Lines.Clear();
        _bufferedBytes = 0;
        WasTruncated = false;
    }
}
