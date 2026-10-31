using _06_OrderProcessing.Phase1;
using _06_OrderProcessing.Phase2;
using _06_OrderProcessing.Shared.Exceptions;
using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;
using System.Diagnostics;

namespace _06_OrderProcessing.Phase3;

public class OrderProcessor
{
    private static readonly TimeSpan PaymentTimeout = TimeSpan.FromSeconds(3);

    // Undoing work after a cancelled run must still finish, so it gets its own bounded token.
    private static readonly TimeSpan CleanupLimit = TimeSpan.FromSeconds(30);

    private readonly CustomerService _customers;
    private readonly InventoryService _inventory;
    private readonly PricingService _pricing;
    private readonly IPaymentGateway _gateway;
    private readonly OrderRepository _orders;
    private readonly IEmailService _email;

    public OrderProcessor(
        CustomerService customers,
        InventoryService inventory,
        PricingService pricing,
        IPaymentGateway gateway,
        OrderRepository orders,
        IEmailService email)
    {
        _customers = customers;
        _inventory = inventory;
        _pricing = pricing;
        _gateway = gateway;
        _orders = orders;
        _email = email;
    }

    // What one order has done so far, so a cancellation knows what to undo.
    private sealed class Progress
    {
        public bool StockReserved;
        public string? CapturedTransactionId;
    }

    // Returns true only if the order ends Completed.
    public async Task<bool> ProcessOrderAsync(Order order, CancellationToken ct)
    {
        var progress = new Progress();

        try
        {
            return await RunStepsAsync(order, progress, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await UndoAsync(order, progress);
            return false;
        }
    }

    private async Task<bool> RunStepsAsync(Order order, Progress progress, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();

        // Step 1: customer must exist and be active.
        Customer customer;
        try
        {
            customer = await _customers.GetCustomerAsync(order.CustomerId, ct);
        }
        catch (CustomerNotFoundException ex)
        {
            return await StopAsync(order, OrderStatus.Rejected, ex.Message, 1, "Customer", watch, ct);
        }

        if (!customer.IsActive)
            return await StopAsync(order, OrderStatus.Rejected, "Customer is inactive", 1, "Customer", watch, ct);

        StepOk(order, 1, "Customer", watch, customer.Name);

        // Step 2: reserve stock (all items or nothing).
        watch.Restart();
        if (!await _inventory.ReserveStockAsync(order, ct))
        {
            string? name = await _inventory.FindShortageNameAsync(order, ct);
            return await StopAsync(order, OrderStatus.Rejected,
                $"Insufficient stock for '{name}'", 2, "Stock", watch, ct);
        }

        progress.StockReserved = true;
        StepOk(order, 2, "Stock", watch);

        // Step 3: pricing. A bad coupon rejects the order and puts the stock back.
        watch.Restart();
        try
        {
            await _pricing.CalculateTotalAsync(order, ct);
        }
        catch (InvalidCouponException ex)
        {
            await ReleaseStockOnceAsync(order, progress);
            return await StopAsync(order, OrderStatus.Rejected, ex.Message, 3, "Pricing", watch, ct);
        }

        StepOk(order, 3, "Pricing", watch, $"total {order.TotalAmount:F2}");

        // Step 4: payment, with a 3-second limit. The caller's token is linked to the limit,
        // so a cancelled RUN is told apart from a payment TIMEOUT.
        watch.Restart();
        PaymentResult payment;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(PaymentTimeout);
            try
            {
                payment = await _gateway.ProcessPaymentAsync(order.Id, order.TotalAmount, timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await ReleaseStockOnceAsync(order, progress);
                return await StopAsync(order, OrderStatus.PaymentFailed,
                    "Payment gateway timeout (3s)", 4, "Payment", watch, ct);
            }
        }

        if (!payment.Success)
        {
            await ReleaseStockOnceAsync(order, progress);
            return await StopAsync(order, OrderStatus.PaymentFailed,
                payment.Message ?? "Card declined by issuer", 4, "Payment", watch, ct);
        }

        progress.CapturedTransactionId = payment.TransactionId;
        StepOk(order, 4, "Payment", watch, payment.TransactionId);

        // Step 5: save as Completed. If the save fails, undo the payment and the stock.
        watch.Restart();
        order.TransactionId = payment.TransactionId;
        order.Status = OrderStatus.Completed;
        order.FailureReason = null;
        try
        {
            await _orders.SaveAsync(order, ct);
        }
        catch (DatabaseWriteException)
        {
            await RefundOnceAsync(progress);
            await ReleaseStockOnceAsync(order, progress);
            return await StopAsync(order, OrderStatus.Failed,
                $"Database save failed after payment; payment {payment.TransactionId} refunded",
                5, "Save", watch, ct);
        }

        // From here the order is finished: nothing is undone any more.
        progress.StockReserved = false;
        progress.CapturedTransactionId = null;
        StepOk(order, 5, "Save", watch);

        // Step 6: confirmation email. A failing email never changes the order status.
        watch.Restart();
        try
        {
            await _email.SendConfirmationWithRetryAsync(order, customer, ct);
            StepOk(order, 6, "Email", watch);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Log.Info($"Order #{order.Id} -> Step 6/6 Email ... cancelled, the order stays Completed");
        }

        return true;
    }

    // Cancelled run: undo this order's work, then save it as Pending / "Cancelled" (6.6 R4).
    private async Task UndoAsync(Order order, Progress progress)
    {
        Log.Error($"Order #{order.Id} -> Cancelled: undoing the work done so far");

        await RefundOnceAsync(progress);
        await ReleaseStockOnceAsync(order, progress);

        order.Status = OrderStatus.Pending;
        order.FailureReason = "Cancelled";
        order.TransactionId = null;

        using var cleanup = new CancellationTokenSource(CleanupLimit);
        await _orders.SaveAsync(order, cleanup.Token);
    }

    // The "Once" methods can never undo the same thing twice.
    private async Task RefundOnceAsync(Progress progress)
    {
        string? transactionId = progress.CapturedTransactionId;
        if (transactionId is null)
            return;

        progress.CapturedTransactionId = null;
        using var cleanup = new CancellationTokenSource(CleanupLimit);
        await _gateway.RefundAsync(transactionId, cleanup.Token);
    }

    private async Task ReleaseStockOnceAsync(Order order, Progress progress)
    {
        if (!progress.StockReserved)
            return;

        progress.StockReserved = false;
        using var cleanup = new CancellationTokenSource(CleanupLimit);
        await _inventory.ReleaseStockAsync(order, cleanup.Token);
    }

    private static void StepOk(Order order, int step, string name, Stopwatch watch, string? detail = null)
    {
        string extra = detail is null ? string.Empty : $" ({detail})";
        Log.Success($"Order #{order.Id} -> Step {step}/6 {name} ... OK{extra} in {watch.ElapsedMilliseconds} ms");
    }

    // An order that stops early still gets its final status saved.
    private async Task<bool> StopAsync(
        Order order, OrderStatus status, string reason,
        int step, string name, Stopwatch watch, CancellationToken ct)
    {
        order.Status = status;
        order.FailureReason = reason;

        Log.Error($"Order #{order.Id} -> Step {step}/6 {name} ... {status}: {reason} after {watch.ElapsedMilliseconds} ms");
        await _orders.SaveAsync(order, ct);
        return false;
    }
}