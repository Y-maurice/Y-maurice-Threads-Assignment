using _06_OrderProcessing.Shared.Models;

namespace _06_OrderProcessing.Phase3;

public interface IPaymentGateway
{
    Task<PaymentResult> ProcessPaymentAsync(int orderId, decimal amount, CancellationToken ct);
    Task RefundAsync(string transactionId, CancellationToken ct);
}