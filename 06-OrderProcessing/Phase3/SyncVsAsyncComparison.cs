using _06_OrderProcessing.Phase1;
using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;
using System.Diagnostics;

namespace _06_OrderProcessing.Phase3;

public static class SyncVsAsyncComparison
{
    public static async Task RunAsync(CancellationToken ct)
    {
        var store = new JsonFileStore();

        // ---------- Sync: one Thread per order ----------
        await DataSeeder.ResetAsync(ct);
        List<Order> syncOrders = await LoadFirstTenAsync(store, ct);

        var syncProcessor = new SyncOrderProcessor(
            new SyncCustomerRepository(store),
            new SyncProductRepository(store),
            new SyncPricingService(store, new SyncCustomerRepository(store), new SyncCouponRepository(store)),
            new SyncPaymentGateway(store),
            new SyncOrderRepository(store),
            new SyncEmailService(store));

        Log.ResetThreadTracking();
        int poolBeforeSync = ThreadPool.ThreadCount;
        var syncWatch = Stopwatch.StartNew();

        var threads = new List<Thread>();
        foreach (Order order in syncOrders)
        {
            Order captured = order;
            var thread = new Thread(() => syncProcessor.ProcessOrder(captured));
            threads.Add(thread);
            thread.Start();
        }
        foreach (Thread thread in threads)
            thread.Join();

        syncWatch.Stop();
        int syncThreads = Log.DistinctThreadCount;
        int poolAfterSync = ThreadPool.ThreadCount;
        string syncResult = await SummaryAsync(store, ct);

        // ---------- Async: ten tasks, then await them one by one ----------
        await DataSeeder.ResetAsync(ct);
        List<Order> asyncOrders = await LoadFirstTenAsync(store, ct);
        OrderProcessor asyncProcessor = Phase3Runner.BuildProcessor(store, withFailures: false);

        Log.ResetThreadTracking();
        int poolBeforeAsync = ThreadPool.ThreadCount;
        var asyncWatch = Stopwatch.StartNew();

        var tasks = new List<Task<bool>>();
        foreach (Order order in asyncOrders)
            tasks.Add(asyncProcessor.ProcessOrderAsync(order, ct));
        foreach (Task<bool> task in tasks)
            await task;

        asyncWatch.Stop();
        int asyncThreads = Log.DistinctThreadCount;
        int poolAfterAsync = ThreadPool.ThreadCount;
        string asyncResult = await SummaryAsync(store, ct);

        Console.WriteLine();
        Console.WriteLine(string.Join(Environment.NewLine, new[]
        {
            "===== Sync vs async (orders #1-#10, no failures) =====",
            $"Sync  (10 Threads, Thread.Sleep): {syncWatch.Elapsed.TotalSeconds:F1} s, {syncResult}",
            $"      distinct threads that logged: {syncThreads}, pool threads {poolBeforeSync} -> {poolAfterSync}",
            $"Async (10 tasks, Task.Delay):     {asyncWatch.Elapsed.TotalSeconds:F1} s, {asyncResult}",
            $"      distinct threads that logged: {asyncThreads}, pool threads {poolBeforeAsync} -> {poolAfterAsync}",
        }));
    }

    private static async Task<List<Order>> LoadFirstTenAsync(JsonFileStore store, CancellationToken ct)
    {
        List<Order> all = await new IncomingOrderRepository(store).GetAllAsync(ct);
        return all.Where(o => o.Id <= 10).ToList();
    }

    // Read back from orders.json (not from memory).
    private static async Task<string> SummaryAsync(JsonFileStore store, CancellationToken ct)
    {
        List<Order> saved = await new OrderRepository(store).GetAllAsync(ct);
        int completed = saved.Count(o => o.Status == OrderStatus.Completed);
        int rejected = saved.Count(o => o.Status == OrderStatus.Rejected);
        return $"{completed} Completed, {rejected} Rejected";
    }
}
