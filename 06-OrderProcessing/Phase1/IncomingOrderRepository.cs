using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;

namespace _06_OrderProcessing.Phase1;

public class IncomingOrderRepository
{
    private readonly JsonFileStore _store;

    public IncomingOrderRepository(JsonFileStore store) => _store = store;

    public async Task<List<Order>> GetAllAsync(CancellationToken ct)
    {
        Log.Info("IncomingOrderRepository -> Opening connection...");
        await Task.Delay(200, ct);

        Log.Info("IncomingOrderRepository -> Executing query for incoming orders...");
        await Task.Delay(Random.Shared.Next(300, 601), ct);

        List<Order> orders = await _store.ReadAllAsync<Order>(DataFiles.IncomingOrders, ct);

        Log.Success($"IncomingOrderRepository -> {orders.Count} incoming orders found");
        return orders;
    }
}