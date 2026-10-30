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
        await DataSeeder.ResetAsync(ct);

        var store = new JsonFileStore();
        var customerRepo = new CustomerRepository(store);
        var productRepo = new ProductRepository(store);
        var couponRepo = new CouponRepository(store);

        // Failures are injected from here, so no class checks order ids itself (9.2).
        var orderRepo = new OrderRepository(store, new[] { 13 });
        var processor = new OrderProcessor(
            new CustomerService(customerRepo),
            new InventoryService(productRepo),
            new PricingService(store, customerRepo, couponRepo, Array.Empty<int>()),
            new PaymentGateway(store, timeoutOrderIds: new[] { 5 }, declineOrderIds: new[] { 8 }),
            orderRepo,
            new EmailService(store, new Dictionary<int, int> { [3] = 2, [17] = int.MaxValue }));

        List<Order> orders = await new IncomingOrderRepository(store).GetAllAsync(ct);

        // Start every order, then await them one by one.
        var watch = Stopwatch.StartNew();
        var tasks = new List<Task<bool>>();
        foreach (Order order in orders)
            tasks.Add(processor.ProcessOrderAsync(order, ct));

        foreach (Task<bool> task in tasks)
            await task;
        watch.Stop();

        // Final table, printed with a single call so no log line can cut it.
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
}
