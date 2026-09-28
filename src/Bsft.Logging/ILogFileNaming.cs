namespace Bsft.Logging;

internal interface ILogFileNaming
{
    string AllFilesPattern { get; }

    string FilesForDatePattern(DateOnly date);

    string PathFor(string directory, DateOnly date, int rollIndex);

    LogFileDescriptor? DescribeOrNull(string path);
}
