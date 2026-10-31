using _06_OrderProcessing.Phase1;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;
using System.Diagnostics;

namespace _06_OrderProcessing.Phase2;

public static class Phase2Runner
{
    private static readonly TimeSpan CleanupLimit = TimeSpan.FromSeconds(30);

    public static async Task RunAsync(CancellationToken ct)
    {
        var store = new JsonFileStore();
        var customers = new CustomerRepository(store);
        var products = new ProductRepository(store);
        var coupons = new CouponRepository(store);
        var orderRepo = new OrderRepository(store);
        var incoming = new IncomingOrderRepository(store);

        List<Order> orders = new();
        var started = new List<(Order Order, Task<bool> Task)>();
        var total = new Stopwatch();

        try
        {
            // 1. Reset -> race demo -> reset again -> pricing comparison on order #1 (not saved).
            await DataSeeder.ResetAsync(ct);
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
            Console.WriteLine(string.Join(Environment.NewLine,
                "--- Pricing order #1 ---",
                $"Sequential: {sequentialWatch.Elapsed.TotalSeconds:F1} s",
                $"Concurrent: {concurrentWatch.Elapsed.TotalSeconds:F1} s",
                $"Total: {order1.TotalAmount:F2}"));

            // 2. Validate all 20 orders concurrently and save each one.
            orders = await incoming.GetAllAsync(ct);
            var validator = new OrderValidator(customers, products, coupons);
            total.Start();

            var validationTasks = orders
                .Select(o => ValidateAndSaveAsync(o, validator, orderRepo, ct))
                .ToList();
            foreach (Task task in validationTasks)
                await task;

            // 3. Start ProcessValidatedOrderAsync for every Validated order (none after a cancel).
            var processor = new OrderProcessorV2(products, orderRepo, pricer, new InvoiceGenerator());
            if (!ct.IsCancellationRequested)
            {
                foreach (Order order in orders.Where(o => o.Status == OrderStatus.Validated))
                    started.Add((order, processor.ProcessValidatedOrderAsync(order, ct)));
            }

            if (started.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"Status of task[0] right after creation: {started[0].Task.Status}");
            }

            // 4. While they run, look at every task.
            try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { }

            if (started.Count > 0)
            {
                Console.WriteLine("Status of every task after 1 s:" + Environment.NewLine +
                    string.Join(Environment.NewLine, started.Select(s => $"  Order #{s.Order.Id}: {s.Task.Status}")));
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled during the demo, a reset, the pricing or the load: whatever was started is handled below.
        }

        // 5. Await each started task one by one. A failing order must not stop the others.
        foreach (var (order, task) in started)
        {
            try
            {
                bool completed = await task;
                Console.WriteLine($"Order #{order.Id}: task {task.Status}, result {completed}, order {order.Status}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"Order #{order.Id}: task {task.Status} - {ex.Message}");

                order.Status = OrderStatus.Failed;
                order.FailureReason = ex.Message;

                using var cleanup = new CancellationTokenSource(CleanupLimit);
                foreach (OrderItem item in order.Items)
                    await products.ReleaseStockAsync(item.ProductId, item.Quantity, cleanup.Token);
                await orderRepo.SaveAsync(order, cleanup.Token);
            }
        }
        total.Stop();

        // Cancelled run: every order that is not finished is saved as Pending / "Cancelled" (6.6 R4, R5).
        if (ct.IsCancellationRequested)
        {
            var marks = orders
                .Where(o => (o.Status == OrderStatus.Pending || o.Status == OrderStatus.Validated)
                            && o.FailureReason != "Cancelled")
                .Select(o => MarkCancelledAsync(o, orderRepo))
                .ToList();
            foreach (Task mark in marks)
                await mark;

            int cancelled = orders.Count(o => o.Status == OrderStatus.Pending && o.FailureReason == "Cancelled");
            Console.WriteLine($"Run cancelled — {orders.Count - cancelled} orders finished, {cancelled} cancelled");
            return;
        }

        // 6. Final status of every task and the total time.
        Console.WriteLine();
        Console.WriteLine("Final status of every task:" + Environment.NewLine +
            string.Join(Environment.NewLine, started.Select(s => $"  Order #{s.Order.Id}: {s.Task.Status}")));
        Console.WriteLine($"Total time (validate + process): {total.Elapsed.TotalSeconds:F1} s");

        try
        {
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

            Console.WriteLine(string.Join(Environment.NewLine,
                $"Total orders: {report.TotalOrders}, revenue: {report.TotalRevenue:F2}",
                $"Completed {report.CompletedCount}, Rejected {report.RejectedCount}, " +
                $"Failed {report.FailedCount}, PaymentFailed {report.PaymentFailedCount}",
                $"Top seller: {report.TopSellingProduct}, {report.TopSellingQuantity} units",
                $"Average order value: {report.AverageOrderValue:F2}"));

            BlockingDemo.Run();   // Phase 2 question 3: the only place .Result is used
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"Run cancelled — {orders.Count} orders finished, 0 cancelled");
        }
    }

    private static async Task ValidateAndSaveAsync(
        Order order, OrderValidator validator, OrderRepository orderRepo, CancellationToken ct)
    {
        try
        {
            await validator.ValidateOrderAsync(order, ct);
            await orderRepo.SaveAsync(order, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await MarkCancelledAsync(order, orderRepo);
        }
    }

    private static async Task MarkCancelledAsync(Order order, OrderRepository orderRepo)
    {
        order.Status = OrderStatus.Pending;
        order.FailureReason = "Cancelled";

        using var cleanup = new CancellationTokenSource(CleanupLimit);
        await orderRepo.SaveAsync(order, cleanup.Token);
    }
}