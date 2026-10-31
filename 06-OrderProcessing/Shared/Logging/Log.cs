using System.Collections.Concurrent;

namespace _06_OrderProcessing.Shared.Logging;

public static class Log
{
    private static readonly Lock Gate = new();

    // Ids of the threads that have written at least one log line (used by the sync vs async comparison).
    private static readonly ConcurrentDictionary<int, byte> Threads = new();

    public static int DistinctThreadCount => Threads.Count;

    public static void ResetThreadTracking() => Threads.Clear();

    public static void Info(string message) => Write(message, null);

    public static void Success(string message) => Write(message, ConsoleColor.Green);

    public static void Error(string message) => Write(message, ConsoleColor.Red);

    private static void Write(string message, ConsoleColor? color)
    {
        int threadId = Environment.CurrentManagedThreadId;
        Threads.TryAdd(threadId, 0);

        string line = $"[{DateTime.Now:HH:mm:ss.fff}] [Thread {threadId}] {message}";

        lock (Gate)
        {
            if (color is not null)
                Console.ForegroundColor = color.Value;

            Console.WriteLine(line);

            if (color is not null)
                Console.ResetColor();
        }
    }
}
