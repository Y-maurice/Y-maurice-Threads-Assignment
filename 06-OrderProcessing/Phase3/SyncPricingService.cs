using _06_OrderProcessing.Phase1;
using _06_OrderProcessing.Phase2;
using _06_OrderProcessing.Shared.Exceptions;
using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;

namespace _06_OrderProcessing.Phase3;

public sealed class SyncPricingService
{
    private readonly JsonFileStore _store;
    private readonly SyncCustomerRepository _customers;
    private readonly SyncCouponRepository _coupons;

    public SyncPricingService(JsonFileStore store, SyncCustomerRepository customers, SyncCouponRepository coupons)
    {
        _store = store;
        _customers = customers;
        _coupons = coupons;
    }

    public decimal CalculateTotal(Order order)
    {
        // SubTotal: DB query
        Log.Info($"SyncPricingService -> SubTotal query for Order #{order.Id}...");
        Thread.Sleep(200);
        Thread.Sleep(Random.Shared.Next(300, 601));

        List<Product> products = _store.ReadAll<Product>(DataFiles.Products);
        decimal subTotal = 0m;
        foreach (OrderItem item in order.Items)
        {
            Product product = products.First(p => p.Id == item.ProductId);
            item.UnitPrice = product.Price;
            subTotal += item.UnitPrice * item.Quantity;
        }
        order.SubTotal = PricingService.Round(subTotal);

        // Discount API
        Log.Info($"SyncPricingService -> Discount API for Order #{order.Id}...");
        Thread.Sleep(150);
        Thread.Sleep(300);
        Thread.Sleep(Random.Shared.Next(500, 1501));

        Customer customer = _customers.GetById(order.CustomerId)
            ?? throw new CustomerNotFoundException($"Customer #{order.CustomerId} not found");

        Coupon? coupon = order.CouponCode is null ? null : _coupons.GetByCode(order.CouponCode);
        if (order.CouponCode is not null)
        {
            if (coupon is null)
                throw new InvalidCouponException($"Coupon '{order.CouponCode}' does not exist");
            if (coupon.ExpiryDate < DateTime.Today)
                throw new InvalidCouponException($"Coupon '{order.CouponCode}' has expired");
        }

        int loyalty = customer.LoyaltyTier switch
        {
            LoyaltyTier.Silver => 5,
            LoyaltyTier.Gold => 10,
            _ => 0
        };
        int percent = Math.Min(loyalty + (coupon?.DiscountPercent ?? 0), 30);
        order.Discount = PricingService.Round(order.SubTotal * percent / 100m);

        // Shipping API
        Log.Info($"SyncPricingService -> Shipping API for Order #{order.Id}...");
        Thread.Sleep(150);
        Thread.Sleep(300);
        Thread.Sleep(Random.Shared.Next(500, 1501));

        products = _store.ReadAll<Product>(DataFiles.Products);
        int quantity = order.Items.Sum(i => i.Quantity);
        decimal weight = order.Items.Sum(i => i.Quantity * products.First(p => p.Id == i.ProductId).WeightKg);
        order.ShippingCost = order.SubTotal >= 1000m
            ? 0m
            : PricingService.Round(5.00m + 1.50m * quantity + 2.00m * weight);

        order.TotalAmount = PricingService.Round(order.SubTotal - order.Discount + order.ShippingCost);
        Log.Success($"SyncPricingService -> Order #{order.Id} total {order.TotalAmount:F2}");
        return order.TotalAmount;
    }
}
