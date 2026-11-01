using _06_OrderProcessing.Phase1;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;

namespace _06_OrderProcessing.Phase2;

public static class RaceConditionDemo
{
    private const int KeyboardId = 4;

    public static async Task RunAsync(JsonFileStore store, ProductRepository products, CancellationToken ct)
    {
        // Round 1: the unsafe reservation
        Console.WriteLine();
        Console.WriteLine("--- Last keyboard: UNSAFE reservation ---");
        await SetKeyboardStockAsync(store, 1, ct);

        Task<bool> unsafe10 = products.ReserveStockUnsafeAsync(KeyboardId, 1, ct);
        Task<bool> unsafe11 = products.ReserveStockUnsafeAsync(KeyboardId, 1, ct);
        bool unsafeResult10 = await unsafe10;
        bool unsafeResult11 = await unsafe11;
        await PrintResultAsync(products, unsafeResult10, unsafeResult11, ct);

        // Round 2: the safe reservation from a fresh stock of 1.
        Console.WriteLine();
        Console.WriteLine("--- Last keyboard: SAFE reservation ---");
        await SetKeyboardStockAsync(store, 1, ct);

        Task<bool> safe10 = products.ReserveStockAsync(KeyboardId, 1, ct);
        Task<bool> safe11 = products.ReserveStockAsync(KeyboardId, 1, ct);
        bool safeResult10 = await safe10;
        bool safeResult11 = await safe11;
        await PrintResultAsync(products, safeResult10, safeResult11, ct);
    }

    private static async Task SetKeyboardStockAsync(JsonFileStore store, int stock, CancellationToken ct)
    {
        await store.UpdateAsync<Product, bool>(DataFiles.Products, products =>
        {
            products.First(p => p.Id == KeyboardId).StockQuantity = stock;
            return true;
        }, ct);
    }

    private static async Task PrintResultAsync(
        ProductRepository products, bool order10, bool order11, CancellationToken ct)
    {
        Product? keyboard = await products.GetByIdAsync(KeyboardId, ct);

        Console.WriteLine($"Order #10 got the keyboard: {order10}");
        Console.WriteLine($"Order #11 got the keyboard: {order11}");
        Console.WriteLine($"Stock read back from products.json: {keyboard?.StockQuantity}");
    }
}
