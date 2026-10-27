namespace _06_OrderProcessing.Shared.Logging;

public static class Log
{
    private static readonly Lock Gate = new();

    public static void Info(string message) => Write(message, null);

    public static void Success(string message) => Write(message, ConsoleColor.Green);

    public static void Error(string message) => Write(message, ConsoleColor.Red);

    private static void Write(string message, ConsoleColor? color)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] [Thread {Environment.CurrentManagedThreadId}] {message}";

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
