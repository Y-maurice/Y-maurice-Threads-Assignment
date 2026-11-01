using _06_OrderProcessing.Shared.Models;

namespace _06_OrderProcessing.Phase3;

public interface IEmailService
{
    Task SendConfirmationAsync(Order order, Customer customer, CancellationToken ct);
    Task SendConfirmationWithRetryAsync(Order order, Customer customer, CancellationToken ct);
}