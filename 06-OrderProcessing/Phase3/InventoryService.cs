using _06_OrderProcessing.Phase1;
using _06_OrderProcessing.Shared.Models;

namespace _06_OrderProcessing.Phase3;

public class InventoryService
{
    private static readonly TimeSpan CleanupLimit = TimeSpan.FromSeconds(30);

    private readonly ProductRepository _products;

    public InventoryService(ProductRepository products) => _products = products;

    // All items or nothing: if one item fails or the run is cancelled, the ones already reserved are put back.
    public async Task<bool> ReserveStockAsync(Order order, CancellationToken ct)
    {
        var reserved = new List<OrderItem>();

        try
        {
            foreach (OrderItem item in order.Items)
            {
                if (!await _products.ReserveStockAsync(item.ProductId, item.Quantity, ct))
                {
                    await PutBackAsync(reserved);
                    return false;
                }

                reserved.Add(item);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            await PutBackAsync(reserved);
            throw;
        }
    }

    // Adds every item's quantity back.
    public async Task ReleaseStockAsync(Order order, CancellationToken ct)
    {
        foreach (OrderItem item in order.Items)
            await _products.ReleaseStockAsync(item.ProductId, item.Quantity, ct);
    }

    // Name of the first product that does not have enough stock (used for the Rejected reason).
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

    // Undo work with its own token: the run's token may already be cancelled.
    private async Task PutBackAsync(List<OrderItem> items)
    {
        using var cleanup = new CancellationTokenSource(CleanupLimit);
        foreach (OrderItem item in items)
            await _products.ReleaseStockAsync(item.ProductId, item.Quantity, cleanup.Token);

        items.Clear();
    }
}