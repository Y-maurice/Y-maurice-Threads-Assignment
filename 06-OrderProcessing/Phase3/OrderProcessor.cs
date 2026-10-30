using _06_OrderProcessing.Phase1;
using _06_OrderProcessing.Phase2;
using _06_OrderProcessing.Shared.Exceptions;
using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;
using System.Diagnostics;

namespace _06_OrderProcessing.Phase3;

public sealed class OrderProcessor
{
    private static readonly TimeSpan PaymentTimeout = TimeSpan.FromSeconds(3);

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

    // Returns true only if the order ends Completed.
    public async Task<bool> ProcessOrderAsync(Order order, CancellationToken ct)
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

        StepOk(order, 2, "Stock", watch);

        // Step 3: pricing. A bad coupon rejects the order and puts the stock back.
        watch.Restart();
        try
        {
            await _pricing.CalculateTotalAsync(order, ct);
        }
        catch (InvalidCouponException ex)
        {
            await _inventory.ReleaseStockAsync(order, ct);
            return await StopAsync(order, OrderStatus.Rejected, ex.Message, 3, "Pricing", watch, ct);
        }

        StepOk(order, 3, "Pricing", watch, $"total {order.TotalAmount:F2}");

        // Step 4: payment, with a 3-second limit. The limit is a token that cancels itself;
        // the caller's own token is linked to it, so a cancelled RUN is still told apart.
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
                // Only our 3 s limit fired. (A cancelled run is not caught here: see 6.6.)
                await _inventory.ReleaseStockAsync(order, ct);
                return await StopAsync(order, OrderStatus.PaymentFailed,
                    "Payment gateway timeout (3s)", 4, "Payment", watch, ct);
            }
        }

        if (!payment.Success)
        {
            await _inventory.ReleaseStockAsync(order, ct);
            return await StopAsync(order, OrderStatus.PaymentFailed,
                payment.Message ?? "Card declined by issuer", 4, "Payment", watch, ct);
        }

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
            await _gateway.RefundAsync(payment.TransactionId!, ct);
            await _inventory.ReleaseStockAsync(order, ct);
            return await StopAsync(order, OrderStatus.Failed,
                $"Database save failed after payment; payment {payment.TransactionId} refunded",
                5, "Save", watch, ct);
        }

        StepOk(order, 5, "Save", watch);

        // Step 6: confirmation email. A failing email never changes the order status.
        watch.Restart();
        await _email.SendConfirmationWithRetryAsync(order, customer, ct);
        StepOk(order, 6, "Email", watch);

        return order.Status == OrderStatus.Completed;
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