using _06_OrderProcessing.Shared.Exceptions;
using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;

namespace _06_OrderProcessing.Phase1;

public class OrderRepository
{
    private readonly JsonFileStore _store;
    private readonly HashSet<int> _failSaveForOrderIds;

    public OrderRepository(JsonFileStore store, IEnumerable<int>? failSaveForOrderIds = null)
    {
        _store = store;
        _failSaveForOrderIds = (failSaveForOrderIds ?? Array.Empty<int>()).ToHashSet();
    }

    public async Task SaveAsync(Order order, CancellationToken ct)
    {
        Log.Info("OrderRepository -> Opening connection...");
        await Task.Delay(200, ct);

        Log.Info("OrderRepository -> Beginning transaction...");
        await Task.Delay(100, ct);

        Log.Info($"OrderRepository -> Saving Order #{order.Id} ({order.Status})...");
        await Task.Delay(400, ct);

        //  injected failure, only when saving this order as Completed.
        if (order.Status == OrderStatus.Completed && _failSaveForOrderIds.Contains(order.Id))
        {
            Log.Error($"OrderRepository -> Commit failed for Order #{order.Id}");
            throw new DatabaseWriteException("Connection lost during commit");
        }

        await _store.UpdateAsync<Order, bool>(DataFiles.Orders, orders =>
        {
            int index = orders.FindIndex(o => o.Id == order.Id);
            if (index >= 0)
                orders[index] = order;
            else
                orders.Add(order);
            return true;
        }, ct);

        Log.Info("OrderRepository -> Committing...");
        await Task.Delay(200, ct);

        Log.Success($"OrderRepository -> Order #{order.Id} saved");
    }
    public async Task<List<Order>> GetAllAsync(CancellationToken ct)
    {
        Log.Info("OrderRepository -> Opening connection...");
        await Task.Delay(200, ct);

        Log.Info("OrderRepository -> Executing query for all orders...");
        await Task.Delay(Random.Shared.Next(300, 601), ct);

        List<Order> orders = await _store.ReadAllAsync<Order>(DataFiles.Orders, ct);

        Log.Success($"OrderRepository -> {orders.Count} orders found");
        return orders;
    }

    public async Task<List<Order>> GetByStatusAsync(OrderStatus status, CancellationToken ct)
    {
        Log.Info("OrderRepository -> Opening connection...");
        await Task.Delay(200, ct);

        Log.Info($"OrderRepository -> Executing query for orders with status {status}...");
        await Task.Delay(Random.Shared.Next(300, 601), ct);

        List<Order> orders = await _store.ReadAllAsync<Order>(DataFiles.Orders, ct);
        List<Order> matching = orders.Where(o => o.Status == status).ToList();

        Log.Success($"OrderRepository -> {matching.Count} orders with status {status}");
        return matching;
    }
}