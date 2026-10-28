using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;

namespace _06_OrderProcessing.Phase1;

public class SyncCustomerRepository
{
    private readonly JsonFileStore _store;

    public SyncCustomerRepository(JsonFileStore store) => _store = store;

    public Customer? GetById(int id)
    {
        Log.Info("SyncCustomerRepository -> Opening connection...");
        Thread.Sleep(200);

        Log.Info($"SyncCustomerRepository -> Executing query for Customer #{id}...");
        Thread.Sleep(Random.Shared.Next(300, 601));

        Customer? customer = _store.ReadAll<Customer>(DataFiles.Customers)
            .FirstOrDefault(c => c.Id == id);

        if (customer is null)
            Log.Info($"SyncCustomerRepository -> Customer #{id} not found");
        else
            Log.Success($"SyncCustomerRepository -> Customer #{id} found ({customer.Name})");

        return customer;
    }
}
