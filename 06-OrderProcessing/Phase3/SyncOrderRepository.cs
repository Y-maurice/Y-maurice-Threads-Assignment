using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;

namespace _06_OrderProcessing.Phase3;

public class SyncOrderRepository
{
    private readonly JsonFileStore _store;

    public SyncOrderRepository(JsonFileStore store) => _store = store;

    public void Save(Order order)
    {
        Log.Info("SyncOrderRepository -> Opening connection...");
        Thread.Sleep(200);
        Log.Info("SyncOrderRepository -> Beginning transaction...");
        Thread.Sleep(100);
        Log.Info($"SyncOrderRepository -> Saving Order #{order.Id} ({order.Status})...");
        Thread.Sleep(400);

        _store.Update<Order, bool>(DataFiles.Orders, orders =>
        {
            int index = orders.FindIndex(o => o.Id == order.Id);
            if (index >= 0)
                orders[index] = order;
            else
                orders.Add(order);
            return true;
        });

        Log.Info("SyncOrderRepository -> Committing...");
        Thread.Sleep(200);
        Log.Success($"SyncOrderRepository -> Order #{order.Id} saved");
    }
}
