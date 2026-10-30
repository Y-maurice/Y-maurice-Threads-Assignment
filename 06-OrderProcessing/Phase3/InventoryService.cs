using _06_OrderProcessing.Phase1;
using _06_OrderProcessing.Shared.Models;

namespace _06_OrderProcessing.Phase3;

public class InventoryService
{
    private readonly ProductRepository _products;

    public InventoryService(ProductRepository products) => _products = products;

    // All items or nothing: if one item fails, the ones already reserved are put back.
    public async Task<bool> ReserveStockAsync(Order order, CancellationToken ct)
    {
        var reserved = new List<OrderItem>();

        foreach (OrderItem item in order.Items)
        {
            bool ok = await _products.ReserveStockAsync(item.ProductId, item.Quantity, ct);

            if (!ok)
            {
                foreach (OrderItem done in reserved)
                    await _products.ReleaseStockAsync(done.ProductId, done.Quantity, ct);
                return false;
            }

            reserved.Add(item);
        }

        return true;
    }

    // Adds every item's quantity back.
    public async Task ReleaseStockAsync(Order order, CancellationToken ct)
    {
        foreach (OrderItem item in order.Items)
            await _products.ReleaseStockAsync(item.ProductId, item.Quantity, ct);
    }


    // Name of the first product that does not have enough stock (used for the Rejected reason)

    public async Task<string?> FindShortageNameAsync(Order order, CancellationToken ct)
    {
        foreach (OrderItem item in order.Items)
        {
            Product? product = await _products.GetByIdAsync(item.ProductId, ct);
            if (product is not null && product.StockQuantity < item.Quantity)
                return product.Name;
        }

        return null;
    }
}