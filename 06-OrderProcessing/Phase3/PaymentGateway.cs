using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;

namespace _06_OrderProcessing.Phase3;

public sealed class PaymentGateway : IPaymentGateway
{
    private readonly JsonFileStore _store;
    private readonly HashSet<int> _timeoutOrderIds;
    private readonly HashSet<int> _declineOrderIds;

    public PaymentGateway(
        JsonFileStore store,
        IEnumerable<int>? timeoutOrderIds = null,
        IEnumerable<int>? declineOrderIds = null)
    {
        _store = store;
        _timeoutOrderIds = (timeoutOrderIds ?? Array.Empty<int>()).ToHashSet();
        _declineOrderIds = (declineOrderIds ?? Array.Empty<int>()).ToHashSet();
    }

    // Payment gateway: authorize 800 -> fraud check 500 -> capture 700
    public async Task<PaymentResult> ProcessPaymentAsync(int orderId, decimal amount, CancellationToken ct)
    {
        Log.Info($"PaymentGateway -> Authorizing payment for Order #{orderId}...");
        await Task.Delay(800, ct);

        Log.Info($"PaymentGateway -> Fraud check for Order #{orderId}...");
        await Task.Delay(500, ct);

        // Injected decline: after the fraud check.
        if (_declineOrderIds.Contains(orderId))
        {
            await AppendAsync(new PaymentTransaction
            {
                OrderId = orderId,
                Amount = amount,
                Status = PaymentStatus.Declined,
                TransactionId = null,
                CreatedAt = DateTime.Now
            }, ct);

            Log.Error($"PaymentGateway -> Payment declined for Order #{orderId}");
            return new PaymentResult
            {
                Success = false,
                Status = PaymentStatus.Declined,
                Message = "Card declined by issuer"
            };
        }

        // Injected timeout: the capture step takes 5000 ms instead of 700.
        int captureMs = _timeoutOrderIds.Contains(orderId) ? 5000 : 700;
        Log.Info($"PaymentGateway -> Capturing payment for Order #{orderId}...");
        await Task.Delay(captureMs, ct);

        // The record is written only after capture finishes, so a timed-out order has none.
        string transactionId = "TXN-" + Guid.NewGuid().ToString("N")[..12].ToUpper();

        await AppendAsync(new PaymentTransaction
        {
            OrderId = orderId,
            Amount = amount,
            Status = PaymentStatus.Captured,
            TransactionId = transactionId,
            CreatedAt = DateTime.Now
        }, ct);

        Log.Success($"PaymentGateway -> Order #{orderId} captured ({transactionId})");
        return new PaymentResult
        {
            Success = true,
            TransactionId = transactionId,
            Status = PaymentStatus.Captured
        };
    }

    // External API: handshake 150 -> send request 300 -> wait for response 500-1500
    public async Task RefundAsync(string transactionId, CancellationToken ct)
    {
        Log.Info($"PaymentGateway -> Refund API: DNS/handshake ({transactionId})...");
        await Task.Delay(150, ct);

        Log.Info($"PaymentGateway -> Refund API: sending request ({transactionId})...");
        await Task.Delay(300, ct);

        Log.Info($"PaymentGateway -> Refund API: waiting for response ({transactionId})...");
        await Task.Delay(Random.Shared.Next(500, 1501), ct);

        bool found = await _store.UpdateAsync<PaymentTransaction, bool>(DataFiles.Payments, payments =>
        {
            PaymentTransaction? payment = payments.FirstOrDefault(p => p.TransactionId == transactionId);
            if (payment is null)
                return false;

            payment.Status = PaymentStatus.Refunded;
            return true;
        }, ct);

        if (!found)
            throw new InvalidOperationException($"Transaction {transactionId} not found");

        Log.Success($"PaymentGateway -> Refunded {transactionId}");
    }

    private Task AppendAsync(PaymentTransaction payment, CancellationToken ct) =>
        _store.UpdateAsync<PaymentTransaction, bool>(DataFiles.Payments, payments =>
        {
            payments.Add(payment);
            return true;
        }, ct);
}
