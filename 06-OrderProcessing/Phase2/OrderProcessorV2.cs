using _06_OrderProcessing.Phase1;
using _06_OrderProcessing.Shared.Models;

namespace _06_OrderProcessing.Phase2;

public sealed class OrderProcessorV2
{
    private static readonly TimeSpan CleanupLimit = TimeSpan.FromSeconds(30);

    private readonly ProductRepository _products;
    private readonly OrderRepository _orders;
    private readonly OrderPricer _pricer;
    private readonly InvoiceGenerator _invoices;

    public OrderProcessorV2(
        ProductRepository products,
        OrderRepository orders,
        OrderPricer pricer,
        InvoiceGenerator invoices)
    {
        _products = products;
        _orders = orders;
        _pricer = pricer;
        _invoices = invoices;
    }

    // Does NOT catch failures: a failing order throws, and the runner handles it.
    // The only exception it handles is a cancelled run (6.6 R4).
    public async Task<bool> ProcessValidatedOrderAsync(Order order, CancellationToken ct)
    {
        var reserved = new List<OrderItem>();

        try
        {
            // 1. Reserve stock for each item, in item order, with the safe method.
            foreach (OrderItem item in order.Items)
            {
                bool ok = await _products.ReserveStockAsync(item.ProductId, item.Quantity, ct);

                if (!ok)
                {
                    await PutBackAsync(reserved);

                    Product? product = await _products.GetByIdAsync(item.ProductId, ct);
                    order.Status = OrderStatus.Rejected;
                    order.FailureReason = $"Insufficient stock for '{product?.Name}'";
                    await _orders.SaveAsync(order, ct);
                    return false;
                }

                reserved.Add(item);
            }

            // 2. Price it (SubTotal, then discount and shipping together).
            await _pricer.PriceOrderAsync(order, ct);

            // 3. Invoice (CPU-bound step).
            await _invoices.GenerateAsync(order, ct);

            // 4. Completed, saved.
            order.Status = OrderStatus.Completed;
            order.FailureReason = null;
            await _orders.SaveAsync(order, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await PutBackAsync(reserved);

            order.Status = OrderStatus.Pending;
            order.FailureReason = "Cancelled";

            using var cleanup = new CancellationTokenSource(CleanupLimit);
            await _orders.SaveAsync(order, cleanup.Token);
            return false;
        }
    }

    // Undo work with its own token: the run's token may already be cancelled.
    private async Task PutBackAsync(List<OrderItem> items)
    {
        if (items.Count == 0)
            return;

        using var cleanup = new CancellationTokenSource(CleanupLimit);
        foreach (OrderItem item in items)
            await _products.ReleaseStockAsync(item.ProductId, item.Quantity, cleanup.Token);

        items.Clear();
    }
}