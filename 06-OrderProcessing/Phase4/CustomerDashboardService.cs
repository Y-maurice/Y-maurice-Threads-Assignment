using _06_OrderProcessing.Phase1;
using _06_OrderProcessing.Shared.Exceptions;
using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;

namespace _06_OrderProcessing.Phase4;



public class CustomerDashboard
{
    public Customer Customer { get; set; } = new();
    public List<Order> Orders { get; set; } = new();
    public List<PaymentTransaction> Payments { get; set; } = new();
    public List<EmailMessage> Emails { get; set; } = new();
}
public sealed class CustomerDashboardService
{
    private readonly JsonFileStore _store;
    private readonly CustomerRepository _customers;
    private readonly OrderRepository _orders;
    private readonly HashSet<int> _failPaymentsForCustomerIds;

    public CustomerDashboardService(
        JsonFileStore store,
        CustomerRepository customers,
        OrderRepository orders,
        IEnumerable<int>? failPaymentsForCustomerIds = null)
    {
        _store = store;
        _customers = customers;
        _orders = orders;
        _failPaymentsForCustomerIds = (failPaymentsForCustomerIds ?? Array.Empty<int>()).ToHashSet();
    }

    public async Task<CustomerDashboard> LoadAsync(int customerId, CancellationToken ct)
    {
        // The four loads start at the same time.
        Task<Customer> profile = LoadProfileAsync(customerId, ct);
        Task<List<Order>> orders = LoadOrdersAsync(customerId, ct);
        Task<List<PaymentTransaction>> payments = LoadPaymentsAsync(customerId, ct);
        Task<List<EmailMessage>> emails = LoadEmailsAsync(customerId, ct);

        // One await for all of them. A plain await would rethrow only the FIRST failure,
        // so the combined task is kept and its Exception holds every failure.
        Task all = Task.WhenAll(profile, orders, payments, emails);
        try
        {
            await all;
        }
        catch when (all.Exception is not null)
        {
            throw new AggregateException(
                $"Dashboard of customer #{customerId} failed", all.Exception.InnerExceptions);
        }

        return new CustomerDashboard
        {
            Customer = await profile,
            Orders = await orders,
            Payments = await payments,
            Emails = await emails
        };
    }

    private async Task<Customer> LoadProfileAsync(int customerId, CancellationToken ct)
    {
        Customer? customer = await _customers.GetByIdAsync(customerId, ct);
        return customer ?? throw new CustomerNotFoundException($"Customer #{customerId} not found");
    }

    private async Task<List<Order>> LoadOrdersAsync(int customerId, CancellationToken ct)
    {
        List<Order> orders = await _orders.GetAllAsync(ct);
        return orders.Where(o => o.CustomerId == customerId).ToList();
    }

    private async Task<List<PaymentTransaction>> LoadPaymentsAsync(int customerId, CancellationToken ct)
    {
        await QueryDelaysAsync("payments", customerId, ct);

        if (_failPaymentsForCustomerIds.Contains(customerId))
        {
            Log.Error($"CustomerDashboardService -> Payments store timeout for Customer #{customerId}");
            throw new TimeoutException("Payments store timeout");
        }

        HashSet<int> orderIds = await OrderIdsOfAsync(customerId, ct);
        List<PaymentTransaction> payments = await _store.ReadAllAsync<PaymentTransaction>(DataFiles.Payments, ct);
        return payments.Where(p => orderIds.Contains(p.OrderId)).ToList();
    }

    private async Task<List<EmailMessage>> LoadEmailsAsync(int customerId, CancellationToken ct)
    {
        await QueryDelaysAsync("emails", customerId, ct);

        HashSet<int> orderIds = await OrderIdsOfAsync(customerId, ct);
        List<EmailMessage> emails = await _store.ReadAllAsync<EmailMessage>(DataFiles.Emails, ct);
        return emails.Where(e => orderIds.Contains(e.OrderId)).ToList();
    }

    // DB query (6.5): open connection 200 -> execute query 300-600.
    private static async Task QueryDelaysAsync(string what, int customerId, CancellationToken ct)
    {
        Log.Info($"CustomerDashboardService -> Opening connection ({what} of Customer #{customerId})...");
        await Task.Delay(200, ct);

        Log.Info($"CustomerDashboardService -> Executing query for {what} of Customer #{customerId}...");
        await Task.Delay(Random.Shared.Next(300, 601), ct);
    }

    private async Task<HashSet<int>> OrderIdsOfAsync(int customerId, CancellationToken ct)
    {
        List<Order> orders = await _store.ReadAllAsync<Order>(DataFiles.Orders, ct);
        return orders.Where(o => o.CustomerId == customerId).Select(o => o.Id).ToHashSet();
    }
}
