using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;
using System.Diagnostics;

namespace _06_OrderProcessing.Phase1;

public static class Phase1Runner
{
    public static async Task RunAsync(CancellationToken ct)
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
        foreach (Order order in orders)
            syncValidator.ValidateOrder(order);
        syncWatch.Stop();

        // 3. Reset again, and load fresh orders (the sync run changed the objects in memory).
        await DataSeeder.ResetAsync(ct);
        orders = await incoming.GetAllAsync(ct);

        // 4. Async run: foreach + await, one by one. ValidateOrderAsync also saves each order.
        var validator = new OrderValidator(customers, products, coupons);

        var asyncWatch = Stopwatch.StartNew();
        foreach (Order order in orders)
        {
            await validator.ValidateOrderAsync(order, ct);
            await orderRepo.SaveAsync(order, ct);
        }
        asyncWatch.Stop();

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
