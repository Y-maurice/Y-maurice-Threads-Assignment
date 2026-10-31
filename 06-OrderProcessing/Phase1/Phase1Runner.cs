using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;
using System.Diagnostics;

namespace _06_OrderProcessing.Phase1;

public static class Phase1Runner
{
    private static readonly TimeSpan CleanupLimit = TimeSpan.FromSeconds(30);

    private sealed class RunCounts
    {
        public int Finished;
        public int Cancelled;
    }

    public static async Task RunAsync(CancellationToken ct)
    {
        var counts = new RunCounts();

        try
        {
            await RunCoreAsync(counts, ct);
        }
        catch (OperationCanceledException)
        {
            // cancelled while resetting, loading or reading back: the counts so far are printed below
        }

        if (ct.IsCancellationRequested)
            Console.WriteLine($"Run cancelled — {counts.Finished} orders finished, {counts.Cancelled} cancelled");
    }

    private static async Task RunCoreAsync(RunCounts counts, CancellationToken ct)
    {
        // 1. Reset and load the 20 incoming orders.
        await DataSeeder.ResetAsync(ct);

        var store = new JsonFileStore();
        var customers = new CustomerRepository(store);
        var products = new ProductRepository(store);
        var coupons = new CouponRepository(store);
        var orderRepo = new OrderRepository(store);
        var incoming = new IncomingOrderRepository(store);

        List<Order> orders = await incoming.GetAllAsync(ct);

        // 2. Sync run: validate all 20 one by one, results not saved.
        var syncValidator = new SyncOrderValidator(
            new SyncCustomerRepository(store),
            new SyncProductRepository(store),
            new SyncCouponRepository(store));

        var syncWatch = Stopwatch.StartNew();
        int syncDone = 0;
        foreach (Order order in orders)
        {
            if (ct.IsCancellationRequested)
                break;   // no new order is started

            syncValidator.ValidateOrder(order);
            syncDone++;
        }
        syncWatch.Stop();

        if (ct.IsCancellationRequested)
        {
            counts.Finished = syncDone;
            counts.Cancelled = orders.Count - syncDone;
            return;
        }

        // 3. Reset again, and load fresh orders (the sync run changed the objects in memory).
        await DataSeeder.ResetAsync(ct);
        orders = await incoming.GetAllAsync(ct);

        // 4. Async run: foreach + await, one by one. Each order is validated, then saved.
        var validator = new OrderValidator(customers, products, coupons);

        var asyncWatch = Stopwatch.StartNew();
        foreach (Order order in orders)
        {
            if (ct.IsCancellationRequested)
            {
                counts.Cancelled++;   // never started
                continue;
            }

            try
            {
                await validator.ValidateOrderAsync(order, ct);
                await orderRepo.SaveAsync(order, ct);
                counts.Finished++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The order in progress stops here: saved as Pending / "Cancelled" (6.6 R4).
                order.Status = OrderStatus.Pending;
                order.FailureReason = "Cancelled";

                using var cleanup = new CancellationTokenSource(CleanupLimit);
                await orderRepo.SaveAsync(order, cleanup.Token);
                counts.Cancelled++;
            }
        }
        asyncWatch.Stop();

        if (ct.IsCancellationRequested)
            return;

        // 5. Summary, read back from orders.json (not from memory).
        List<Order> saved = await orderRepo.GetAllAsync(ct);
        List<Order> validated = await orderRepo.GetByStatusAsync(OrderStatus.Validated, ct);
        List<Order> rejected = await orderRepo.GetByStatusAsync(OrderStatus.Rejected, ct);

        Console.WriteLine();
        Console.WriteLine("===== Validation Summary =====");
        Console.WriteLine($"Total Orders: {orders.Count}");
        Console.WriteLine($"Validated:    {validated.Count}");
        Console.WriteLine($"Rejected:     {rejected.Count}");
        foreach (Order order in rejected.OrderBy(o => o.Id))
            Console.WriteLine($"  - Order #{order.Id}:  {order.FailureReason}");
        Console.WriteLine($"Sync Time:  {syncWatch.Elapsed.TotalSeconds:F1} seconds");
        Console.WriteLine($"Async Time: {asyncWatch.Elapsed.TotalSeconds:F1} seconds");
        Console.WriteLine($"orders.json now contains {saved.Count} records.");
    }
}