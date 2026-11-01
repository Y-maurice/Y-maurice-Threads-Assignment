using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;

namespace _06_OrderProcessing.Phase1;

public sealed class CustomerRepository
{
    private readonly JsonFileStore _store;

    public CustomerRepository(JsonFileStore store) => _store = store;

    public async Task<Customer?> GetByIdAsync(int id, CancellationToken ct)
    {

        Log.Info("CustomerRepository -> Opening connection...");
        await Task.Delay(200, ct);

        Log.Info($"CustomerRepository -> Executing query for Customer #{id}...");
        await Task.Delay(Random.Shared.Next(300, 601), ct);


        List<Customer> customers = await _store.ReadAllAsync<Customer>(DataFiles.Customers, ct);
        Customer? customer = customers.FirstOrDefault(c => c.Id == id);

        if (customer is null)
            Log.Info($"CustomerRepository -> Customer #{id} not found");
        else
            Log.Success($"CustomerRepository -> Customer #{id} found ({customer.Name})");

        return customer;
    }
}
