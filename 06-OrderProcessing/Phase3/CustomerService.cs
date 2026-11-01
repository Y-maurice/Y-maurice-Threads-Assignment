using _06_OrderProcessing.Phase1;
using _06_OrderProcessing.Shared.Exceptions;
using _06_OrderProcessing.Shared.Models;

namespace _06_OrderProcessing.Phase3;

public class CustomerService
{
    private readonly CustomerRepository _customers;

    public CustomerService(CustomerRepository customers) => _customers = customers;

    // DB query, a missing customer is an error here
    public async Task<Customer> GetCustomerAsync(int id, CancellationToken ct)
    {
        Customer? customer = await _customers.GetByIdAsync(id, ct);

        return customer
            ?? throw new CustomerNotFoundException($"Customer #{id} not found");
    }
}
