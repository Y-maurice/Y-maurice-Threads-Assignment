using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;

namespace _06_OrderProcessing.Phase1;

public class SyncProductRepository
{
    private readonly JsonFileStore _store;

    public SyncProductRepository(JsonFileStore store) => _store = store;

    public Product? GetById(int id)
    {
        Log.Info("SyncProductRepository -> Opening connection...");
        Thread.Sleep(200);

        Log.Info($"SyncProductRepository -> Executing query for Product #{id}...");
        Thread.Sleep(Random.Shared.Next(300, 601));

        Product? product = _store.ReadAll<Product>(DataFiles.Products)
            .FirstOrDefault(p => p.Id == id);

        if (product is null)
            Log.Info($"SyncProductRepository -> Product #{id} not found");
        else
            Log.Success($"SyncProductRepository -> Product #{id} found ({product.Name})");

        return product;
    }


    public bool ReserveStock(int productId, int qty)
    {
        Log.Info("SyncProductRepository -> Opening connection...");
        Thread.Sleep(200);
        Log.Info("SyncProductRepository -> Beginning transaction...");
        Thread.Sleep(100);
        Log.Info($"SyncProductRepository -> Updating stock of Product #{productId} (-{qty})...");
        Thread.Sleep(400);

        bool reserved = _store.Update<Product, bool>(DataFiles.Products, products =>
        {
            Product? product = products.FirstOrDefault(p => p.Id == productId);
            if (product is null || product.StockQuantity < qty)
                return false;

            product.StockQuantity -= qty;
            return true;
        });

        Log.Info("SyncProductRepository -> Committing...");
        Thread.Sleep(200);
        return reserved;
    }

    public void ReleaseStock(int productId, int qty)
    {
        Log.Info("SyncProductRepository -> Opening connection...");
        Thread.Sleep(200);
        Log.Info("SyncProductRepository -> Beginning transaction...");
        Thread.Sleep(100);
        Log.Info($"SyncProductRepository -> Releasing {qty} of Product #{productId}...");
        Thread.Sleep(400);

        _store.Update<Product, bool>(DataFiles.Products, products =>
        {
            Product? product = products.FirstOrDefault(p => p.Id == productId);
            if (product is null)
                return false;

            product.StockQuantity += qty;
            return true;
        });

        Log.Info("SyncProductRepository -> Committing...");
        Thread.Sleep(200);
    }
}
