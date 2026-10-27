namespace _06_OrderProcessing.Shared.Exceptions;

public class DataFileCorruptedException : Exception
{
    public string FileName { get; }

    public DataFileCorruptedException(string fileName, Exception inner)
        : base($"Data file '{fileName}' is corrupted or contains invalid JSON.", inner)
        => FileName = fileName;

    public DataFileCorruptedException(string fileName, string reason)
        : base($"Data file '{fileName}' is corrupted: {reason}")
        => FileName = fileName;
}
