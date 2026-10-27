using _06_OrderProcessing.Shared.Models;

namespace _06_OrderProcessing.Shared.Storage;

public static class DataSeeder
{
    public static async Task ResetAsync(CancellationToken ct)
    {
        if (Directory.Exists(DataFiles.DataDirectory))
            Directory.Delete(DataFiles.DataDirectory, recursive: true);

        var store = new JsonFileStore();

        await SeedAsync(store, DataFiles.Customers, BuildCustomers(), ct);
        await SeedAsync(store, DataFiles.Products, BuildProducts(), ct);
        await SeedAsync(store, DataFiles.Coupons, BuildCoupons(), ct);
        await SeedAsync(store, DataFiles.IncomingOrders, BuildIncomingOrders(), ct);

        // Empty collections are stored as [], never as an empty file.
        await SeedAsync(store, DataFiles.Orders, new List<object>(), ct);
        await SeedAsync(store, DataFiles.Payments, new List<object>(), ct);
        await SeedAsync(store, DataFiles.Emails, new List<object>(), ct);
        await SeedAsync(store, DataFiles.EmailRetry, new List<object>(), ct);
    }

    private static async Task SeedAsync<T>(
        JsonFileStore store, string fileName, List<T> items, CancellationToken ct)
    {
        await store.WriteAllAsync(fileName, items, ct);
        Console.WriteLine($"[Seeder] {fileName} -> {items.Count} records");
    }

    private static List<Customer> BuildCustomers() =>
    [
        new Customer { Id = 1,  Name = "Ahmed Ali",     Email = "ahmed.ali@example.com",     IsActive = true,  LoyaltyTier = LoyaltyTier.Gold },
        new Customer { Id = 2,  Name = "Sara Mohamed",  Email = "sara.mohamed@example.com",  IsActive = true,  LoyaltyTier = LoyaltyTier.Silver },
        new Customer { Id = 3,  Name = "Omar Hassan",   Email = "omar.hassan@example.com",   IsActive = true,  LoyaltyTier = LoyaltyTier.Bronze },
        new Customer { Id = 4,  Name = "Mona Ibrahim",  Email = "mona.ibrahim@example.com",  IsActive = false, LoyaltyTier = LoyaltyTier.Silver },
        new Customer { Id = 5,  Name = "Youssef Adel",  Email = "youssef.adel@example.com",  IsActive = true,  LoyaltyTier = LoyaltyTier.Gold },
        new Customer { Id = 6,  Name = "Nour Khaled",   Email = "nour.khaled@example.com",   IsActive = true,  LoyaltyTier = LoyaltyTier.Bronze },
        new Customer { Id = 7,  Name = "Karim Samir",   Email = "karim.samir@example.com",   IsActive = true,  LoyaltyTier = LoyaltyTier.Silver },
        new Customer { Id = 8,  Name = "Laila Mahmoud", Email = "laila.mahmoud@example.com", IsActive = true,  LoyaltyTier = LoyaltyTier.Gold },
        new Customer { Id = 9,  Name = "Hassan Tarek",  Email = "hassan.tarek@example.com",  IsActive = true,  LoyaltyTier = LoyaltyTier.Bronze },
        new Customer { Id = 10, Name = "Dina Fathy",    Email = "dina.fathy@example.com",    IsActive = true,  LoyaltyTier = LoyaltyTier.Silver },
    ];

    private static List<Product> BuildProducts() =>
    [
        new Product { Id = 1, Name = "Laptop",              Price = 1200.00m, StockQuantity = 10, WeightKg = 2.2m },
        new Product { Id = 2, Name = "Smartphone",          Price = 800.00m,  StockQuantity = 15, WeightKg = 0.4m },
        new Product { Id = 3, Name = "Wireless Mouse",      Price = 25.00m,   StockQuantity = 2,  WeightKg = 0.1m },
        new Product { Id = 4, Name = "Mechanical Keyboard", Price = 90.00m,   StockQuantity = 1,  WeightKg = 0.9m },
        new Product { Id = 5, Name = "USB-C Hub",           Price = 45.00m,   StockQuantity = 30, WeightKg = 0.2m },
        new Product { Id = 6, Name = "Headphones",          Price = 150.00m,  StockQuantity = 12, WeightKg = 0.3m },
        new Product { Id = 7, Name = "27\" Monitor",        Price = 350.00m,  StockQuantity = 6,  WeightKg = 5.0m },
        new Product { Id = 8, Name = "Webcam",              Price = 70.00m,   StockQuantity = 20, WeightKg = 0.2m },
    ];

    private static List<Coupon> BuildCoupons()
    {
        DateTime today = DateTime.Today;

        return
        [
            new Coupon { Code = "SAVE10",    DiscountPercent = 10, ExpiryDate = today.AddDays(180) },
            new Coupon { Code = "WELCOME15", DiscountPercent = 15, ExpiryDate = today.AddDays(90) },
            new Coupon { Code = "FLASH20",   DiscountPercent = 20, ExpiryDate = today.AddDays(7) },
            new Coupon { Code = "OLD20",     DiscountPercent = 20, ExpiryDate = today.AddDays(-30) },
        ];
    }

    private static List<Order> BuildIncomingOrders()
    {
        DateTime now = DateTime.Now;

        return
        [
            new Order { Id = 1, CustomerId = 1, CouponCode = "SAVE10", Status = OrderStatus.Pending, CreatedAt = now,
                Items = [ new OrderItem { ProductId = 1, Quantity = 1 }, new OrderItem { ProductId = 3, Quantity = 1 } ] },

            new Order { Id = 2, CustomerId = 2, CouponCode = null, Status = OrderStatus.Pending, CreatedAt = now,
                Items = [ new OrderItem { ProductId = 2, Quantity = 1 } ] },

            new Order { Id = 3, CustomerId = 3, CouponCode = "WELCOME15", Status = OrderStatus.Pending, CreatedAt = now,
                Items = [ new OrderItem { ProductId = 5, Quantity = 2 }, new OrderItem { ProductId = 8, Quantity = 1 } ] },

            new Order { Id = 4, CustomerId = 4, CouponCode = null, Status = OrderStatus.Pending, CreatedAt = now,
                Items = [ new OrderItem { ProductId = 6, Quantity = 1 } ] },

            new Order { Id = 5, CustomerId = 5, CouponCode = null, Status = OrderStatus.Pending, CreatedAt = now,
                Items = [ new OrderItem { ProductId = 7, Quantity = 1 } ] },

            new Order { Id = 6, CustomerId = 6, CouponCode = "FLASH20", Status = OrderStatus.Pending, CreatedAt = now,
                Items = [ new OrderItem { ProductId = 5, Quantity = 1 } ] },

            new Order { Id = 7, CustomerId = 7, CouponCode = null, Status = OrderStatus.Pending, CreatedAt = now,
                Items = [ new OrderItem { ProductId = 6, Quantity = 1 }, new OrderItem { ProductId = 8, Quantity = 1 } ] },

            new Order { Id = 8, CustomerId = 8, CouponCode = "SAVE10", Status = OrderStatus.Pending, CreatedAt = now,
                Items = [ new OrderItem { ProductId = 1, Quantity = 1 } ] },

            new Order { Id = 9, CustomerId = 9, CouponCode = null, Status = OrderStatus.Pending, CreatedAt = now,
                Items = [ new OrderItem { ProductId = 3, Quantity = 5 } ] },

            new Order { Id = 10, CustomerId = 10, CouponCode = null, Status = OrderStatus.Pending, CreatedAt = now,
                Items = [ new OrderItem { ProductId = 4, Quantity = 1 } ] },

            new Order { Id = 11, CustomerId = 1, CouponCode = null, Status = OrderStatus.Pending, CreatedAt = now,
                Items = [ new OrderItem { ProductId = 4, Quantity = 1 } ] },

            new Order { Id = 12, CustomerId = 2, CouponCode = "OLD20", Status = OrderStatus.Pending, CreatedAt = now,
                Items = [ new OrderItem { ProductId = 2, Quantity = 1 } ] },

            new Order { Id = 13, CustomerId = 3, CouponCode = null, Status = OrderStatus.Pending, CreatedAt = now,
                Items = [ new OrderItem { ProductId = 6, Quantity = 2 } ] },

            new Order { Id = 14, CustomerId = 5, CouponCode = "WELCOME15", Status = OrderStatus.Pending, CreatedAt = now,
                Items = [ new OrderItem { ProductId = 8, Quantity = 2 }, new OrderItem { ProductId = 5, Quantity = 1 } ] },

            new Order { Id = 15, CustomerId = 99, CouponCode = null, Status = OrderStatus.Pending, CreatedAt = now,
                Items = [ new OrderItem { ProductId = 2, Quantity = 1 } ] },

            new Order { Id = 16, CustomerId = 6, CouponCode = null, Status = OrderStatus.Pending, CreatedAt = now,
                Items = [ new OrderItem { ProductId = 7, Quantity = 1 }, new OrderItem { ProductId = 5, Quantity = 1 } ] },

            new Order { Id = 17, CustomerId = 7, CouponCode = "SAVE10", Status = OrderStatus.Pending, CreatedAt = now,
                Items = [ new OrderItem { ProductId = 2, Quantity = 1 } ] },

            new Order { Id = 18, CustomerId = 8, CouponCode = "FAKE50", Status = OrderStatus.Pending, CreatedAt = now,
                Items = [ new OrderItem { ProductId = 6, Quantity = 1 } ] },

            new Order { Id = 19, CustomerId = 9, CouponCode = null, Status = OrderStatus.Pending, CreatedAt = now,
                Items = [ new OrderItem { ProductId = 8, Quantity = 1 } ] },

            new Order { Id = 20, CustomerId = 10, CouponCode = "FLASH20", Status = OrderStatus.Pending, CreatedAt = now,
                Items = [ new OrderItem { ProductId = 1, Quantity = 1 }, new OrderItem { ProductId = 6, Quantity = 1 } ] },
        ];
    }
}