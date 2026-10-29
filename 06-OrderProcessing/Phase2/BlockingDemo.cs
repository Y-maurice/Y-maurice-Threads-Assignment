namespace _06_OrderProcessing.Phase2;

// The ONLY place in the project where .Result / .Wait() is allowed
public static class BlockingDemo
{
    public static void Run()
    {
        Console.WriteLine();
        Console.WriteLine("--- Blocking demo (.Result) ---");
        Console.WriteLine($"Before: thread {Environment.CurrentManagedThreadId}");

        Task<int> slow = SlowAsync();

        // BAD: this thread can do nothing else until the task is finished.
        int result = slow.Result;

        Console.WriteLine($"After .Result: {result}, thread {Environment.CurrentManagedThreadId}");

        // An exception comes back wrapped, unlike with await.
        try
        {
            _ = FailAsync().Result;
        }
        catch (AggregateException ex)
        {
            Console.WriteLine($"Caught {ex.GetType().Name}: {ex.InnerException?.Message}");
        }
    }

    private static async Task<int> SlowAsync()
    {
        await Task.Delay(2000);
        return 42;
    }

    private static async Task<int> FailAsync()
    {
        await Task.Delay(100);
        throw new InvalidOperationException("boom");
    }
}
