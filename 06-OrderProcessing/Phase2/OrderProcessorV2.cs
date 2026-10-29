using _06_OrderProcessing.Phase1;
using _06_OrderProcessing.Shared.Models;

namespace _06_OrderProcessing.Phase2;

public class OrderProcessorV2
{
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

    public async Task<bool> ProcessValidatedOrderAsync(Order order, CancellationToken ct)
    {
        // 1. Reserve stock for each item, in item order, with the safe method.
        var reserved = new List<OrderItem>();
        foreach (OrderItem item in order.Items)
        {
            bool ok = await _products.ReserveStockAsync(item.ProductId, item.Quantity, ct);

            if (!ok)
            {
                // Release the items already reserved.
                foreach (OrderItem done in reserved)
                    await _products.ReleaseStockAsync(done.ProductId, done.Quantity, ct);

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

        // 4. Completed, saved
        order.Status = OrderStatus.Completed;
        order.FailureReason = null;
        await _orders.SaveAsync(order, ct);
        return true;
    }
}