using _06_OrderProcessing.Phase1;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;
using System.Diagnostics;

namespace _06_OrderProcessing.Phase2;

public static class Phase2Runner
{
    public static async Task RunAsync(CancellationToken ct)
    {
        // 1. Reset -> race demo -> reset again -> pricing comparison on order #1 (not saved).
        await DataSeeder.ResetAsync(ct);

        var store = new JsonFileStore();
        var customers = new CustomerRepository(store);
        var products = new ProductRepository(store);
        var coupons = new CouponRepository(store);
        var orderRepo = new OrderRepository(store);
        var incoming = new IncomingOrderRepository(store);

        await RaceConditionDemo.RunAsync(store, products, ct);

        await DataSeeder.ResetAsync(ct);

        var pricing = new PricingService(store, customers, coupons, new[] { 7 });
        var pricer = new OrderPricer(pricing);

        List<Order> sample = await incoming.GetAllAsync(ct);
        Order order1 = sample.First(o => o.Id == 1);

        var sequentialWatch = Stopwatch.StartNew();
        await pricer.PriceOrderSequentialAsync(order1, ct);
        sequentialWatch.Stop();

        var concurrentWatch = Stopwatch.StartNew();
        await pricer.PriceOrderAsync(order1, ct);
        concurrentWatch.Stop();

        Console.WriteLine();
        Console.WriteLine("--- Pricing order #1 ---");
        Console.WriteLine($"Sequential: {sequentialWatch.Elapsed.TotalSeconds:F1} s");
        Console.WriteLine($"Concurrent: {concurrentWatch.Elapsed.TotalSeconds:F1} s");
        Console.WriteLine($"Total: {order1.TotalAmount:F2}");

        // 2. Validate all 20 orders concurrently and save each one.
        List<Order> orders = await incoming.GetAllAsync(ct);
        var validator = new OrderValidator(customers, products, coupons);
        var total = Stopwatch.StartNew();

        var validationTasks = new List<Task>();
        foreach (Order order in orders)
            validationTasks.Add(ValidateAndSaveAsync(order, validator, orderRepo, ct));
        foreach (Task task in validationTasks)
            await task;

        // 3. Start ProcessValidatedOrderAsync for every Validated order.
        var processor = new OrderProcessorV2(products, orderRepo, pricer, new InvoiceGenerator());
        List<Order> validated = orders.Where(o => o.Status == OrderStatus.Validated).ToList();

        var tasks = new List<Task<bool>>();
        foreach (Order order in validated)
            tasks.Add(processor.ProcessValidatedOrderAsync(order, ct));

        Console.WriteLine();
        Console.WriteLine($"Status of task[0] right after creation: {tasks[0].Status}");

        // 4. While they run, look at every task.
        await Task.Delay(1000, ct);
        Console.WriteLine("Status of every task after 1 s:");
        for (int i = 0; i < tasks.Count; i++)
            Console.WriteLine($"  Order #{validated[i].Id}: {tasks[i].Status}");

        // 5. Await each task one by one. A failing order must not stop the others
        for (int i = 0; i < tasks.Count; i++)
        {
            Order order = validated[i];
            try
            {
                bool completed = await tasks[i];
                Console.WriteLine($"Order #{order.Id}: task {tasks[i].Status}, result {completed}, order {order.Status}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"Order #{order.Id}: task {tasks[i].Status} - {ex.Message}");

                order.Status = OrderStatus.Failed;
                order.FailureReason = ex.Message;

                foreach (OrderItem item in order.Items)
                    await products.ReleaseStockAsync(item.ProductId, item.Quantity, ct);

                await orderRepo.SaveAsync(order, ct);
            }
        }
        total.Stop();

        // 6. Final status of every task and the total time.
        Console.WriteLine();
        Console.WriteLine("Final status of every task:");
        for (int i = 0; i < tasks.Count; i++)
            Console.WriteLine($"  Order #{validated[i].Id}: {tasks[i].Status}");
        Console.WriteLine($"Total time (validate + process): {total.Elapsed.TotalSeconds:F1} s");

        // 7. Read orders.json back and confirm exactly 20 records.
        List<Order> saved = await orderRepo.GetAllAsync(ct);
        Console.WriteLine();
        Console.WriteLine($"orders.json now contains {saved.Count} records.");
        Console.WriteLine($"  Completed: {saved.Count(o => o.Status == OrderStatus.Completed)}");
        Console.WriteLine($"  Rejected:  {saved.Count(o => o.Status == OrderStatus.Rejected)}");
        Console.WriteLine($"  Failed:    {saved.Count(o => o.Status == OrderStatus.Failed)}");

        // 8. Report: start it, print a line while it runs, then await it.
        var reports = new ReportGenerator(orderRepo, products);
        Task<Report> reportTask = reports.GenerateAsync(ct);
        Console.WriteLine();
        Console.WriteLine("Generating report...");
        Report report = await reportTask;

        Console.WriteLine($"Total orders: {report.TotalOrders}, revenue: {report.TotalRevenue:F2}");
        Console.WriteLine($"Completed {report.CompletedCount}, Rejected {report.RejectedCount}, " +
                          $"Failed {report.FailedCount}, PaymentFailed {report.PaymentFailedCount}");
        Console.WriteLine($"Top seller: {report.TopSellingProduct}, {report.TopSellingQuantity} units");
        Console.WriteLine($"Average order value: {report.AverageOrderValue:F2}");

        BlockingDemo.Run();
    }

    private static async Task ValidateAndSaveAsync(
        Order order, OrderValidator validator, OrderRepository orderRepo, CancellationToken ct)
    {
        await validator.ValidateOrderAsync(order, ct);
        await orderRepo.SaveAsync(order, ct);
    }



}
