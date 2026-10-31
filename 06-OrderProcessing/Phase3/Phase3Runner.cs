using _06_OrderProcessing.Phase1;
using _06_OrderProcessing.Phase2;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;
using System.Diagnostics;

namespace _06_OrderProcessing.Phase3;

public static class Phase3Runner
{
    public static async Task RunAsync(CancellationToken ct)
    {
        // Sync vs async comparison first 
        await SyncVsAsyncComparison.RunAsync(ct);

        await FireAndForgetRunner.RunAsync(ct);

        await DataSeeder.ResetAsync(ct);

        var store = new JsonFileStore();
        OrderProcessor processor = BuildProcessor(store, withFailures: true);

        List<Order> orders = await new IncomingOrderRepository(store).GetAllAsync(ct);

        // Start every order, then await them one by one.
        var watch = Stopwatch.StartNew();
        var tasks = new List<Task<bool>>();
        foreach (Order order in orders)
            tasks.Add(processor.ProcessOrderAsync(order, ct));

        foreach (Task<bool> task in tasks)
            await task;
        watch.Stop();

        // Final table printed with a single call so no log line can cut it
        IEnumerable<string> rows = orders.OrderBy(o => o.Id).Select(o =>
            $"{o.Id,5}  {o.Status,-13}  {o.TotalAmount,9:F2}  {o.FailureReason}");

        Console.WriteLine();
        Console.WriteLine(string.Join(Environment.NewLine,
            new[] { "Order  Status         Total      Reason" }.Concat(rows)));
        Console.WriteLine($"Total time: {watch.Elapsed.TotalSeconds:F1} s");

        // Read the result back from the file.
        List<Order> saved = await new OrderRepository(store).GetAllAsync(ct);
        Console.WriteLine();
        Console.WriteLine($"orders.json now contains {saved.Count} records.");
        foreach (var group in saved.GroupBy(o => o.Status).OrderBy(g => g.Key))
            Console.WriteLine($"  {group.Key}: {group.Count()}");
    }

    // withFailures: false switches off every injected failure (used by the comparison)
    public static OrderProcessor BuildProcessor(JsonFileStore store, bool withFailures)
    {
        var customerRepo = new CustomerRepository(store);
        var productRepo = new ProductRepository(store);
        var couponRepo = new CouponRepository(store);

        int[] none = Array.Empty<int>();
        var orderRepo = new OrderRepository(store, withFailures ? new[] { 13 } : none);

        return new OrderProcessor(
            new CustomerService(customerRepo),
            new InventoryService(productRepo),
            new PricingService(store, customerRepo, couponRepo, none),
            new PaymentGateway(store,
                timeoutOrderIds: withFailures ? new[] { 5 } : none,
                declineOrderIds: withFailures ? new[] { 8 } : none),
            orderRepo,
            new EmailService(store, withFailures
                ? new Dictionary<int, int> { [3] = 2, [17] = int.MaxValue }
                : new Dictionary<int, int>()));
    }
}