using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;

namespace _06_OrderProcessing.Phase1;

public class ProductRepository
{
    private readonly JsonFileStore _store;

    public ProductRepository(JsonFileStore store) => _store = store;

    public async Task<Product?> GetByIdAsync(int id, CancellationToken ct)
    {

        Log.Info("ProductRepository -> Opening connection...");
        await Task.Delay(200, ct);

        Log.Info($"ProductRepository -> Executing query for Product #{id}...");
        await Task.Delay(Random.Shared.Next(300, 601), ct);

        List<Product> products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct);
        Product? product = products.FirstOrDefault(p => p.Id == id);

        if (product is null)
            Log.Info($"ProductRepository -> Product #{id} not found");
        else
            Log.Success($"ProductRepository -> Product #{id} found ({product.Name})");

        return product;
    }

    public async Task<bool> ReserveStockAsync(int productId, int qty, CancellationToken ct)
    {
        // DB write: open connection 200 -> begin transaction 100 -> update 400 -> commit 200
        Log.Info("ProductRepository -> Opening connection...");
        await Task.Delay(200, ct);

        Log.Info("ProductRepository -> Beginning transaction...");
        await Task.Delay(100, ct);

        Log.Info($"ProductRepository -> Updating stock of Product #{productId} (-{qty})...");
        await Task.Delay(400, ct);

        // The file work happens inside the update step, before commit.
        // UpdateAsync does read, check, subtract and write as ONE step.
        bool reserved = await _store.UpdateAsync<Product, bool>(DataFiles.Products, products =>
        {
            Product? product = products.FirstOrDefault(p => p.Id == productId);

            if (product is null || product.StockQuantity < qty)
                return false;

            product.StockQuantity -= qty;
            return true;
        }, ct);

        Log.Info("ProductRepository -> Committing...");
        await Task.Delay(200, ct);

        if (reserved)
            Log.Success($"ProductRepository -> Reserved {qty} of Product #{productId}");
        else
            Log.Error($"ProductRepository -> Could not reserve {qty} of Product #{productId}");

        return reserved;
    }



    // unsafe on purpose, for the race demo: read, wait, then write, with no protection
    // around the whole sequence
    public async Task<bool> ReserveStockUnsafeAsync(int productId, int qty, CancellationToken ct)
    {
        // 1. Read the stock now. After this the file lock is released.
        List<Product> products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct);
        Product? product = products.FirstOrDefault(p => p.Id == productId);

        if (product is null || product.StockQuantity < qty)
            return false;

        // 2. DB write steps: open connection 200 -> begin transaction 100 -> update 400
        Log.Info("ProductRepository (UNSAFE) -> Opening connection...");
        await Task.Delay(200, ct);

        Log.Info("ProductRepository (UNSAFE) -> Beginning transaction...");
        await Task.Delay(100, ct);

        Log.Info($"ProductRepository (UNSAFE) -> Updating stock of Product #{productId} (-{qty})...");
        await Task.Delay(400, ct);

        // 3. Write back a value calculated from the oldd read
        product.StockQuantity -= qty;
        await _store.WriteAllAsync(DataFiles.Products, products, ct);

        Log.Info("ProductRepository (UNSAFE) -> Committing...");
        await Task.Delay(200, ct);

        Log.Success($"ProductRepository (UNSAFE) -> Reserved {qty} of Product #{productId}");
        return true;
    }

    public async Task ReleaseStockAsync(int productId, int qty, CancellationToken ct)
    {
        // DB write: open connection 200 -> begin transaction 100 -> update 400 -> commit 200
        Log.Info("ProductRepository -> Opening connection...");
        await Task.Delay(200, ct);

        Log.Info("ProductRepository -> Beginning transaction...");
        await Task.Delay(100, ct);

        Log.Info($"ProductRepository -> Releasing {qty} of Product #{productId}...");
        await Task.Delay(400, ct);

        // Atomic read-change-write, same as ReserveStockAsync
        await _store.UpdateAsync<Product, bool>(DataFiles.Products, products =>
        {
            Product? product = products.FirstOrDefault(p => p.Id == productId);
            if (product is null)
                return false;

            product.StockQuantity += qty;
            return true;
        }, ct);

        Log.Info("ProductRepository -> Committing...");
        await Task.Delay(200, ct);

        Log.Success($"ProductRepository -> Released {qty} of Product #{productId}");
    }
}
