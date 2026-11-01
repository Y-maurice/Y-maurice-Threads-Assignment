using _06_OrderProcessing.Phase1;
using _06_OrderProcessing.Shared.Exceptions;
using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;

namespace _06_OrderProcessing.Phase2;

public class PricingService
{
    private readonly JsonFileStore _store;
    private readonly CustomerRepository _customers;
    private readonly CouponRepository _coupons;
    private readonly HashSet<int> _failShippingForOrderIds;

    public PricingService(
        JsonFileStore store,
        CustomerRepository customers,
        CouponRepository coupons,
        IEnumerable<int> failShippingForOrderIds)
    {
        _store = store;
        _customers = customers;
        _coupons = coupons;
        _failShippingForOrderIds = failShippingForOrderIds.ToHashSet();
    }

    // DB query: fills UnitPrice on every item and returns the SubTotal.
    public async Task<decimal> CalculateSubTotalAsync(Order order, CancellationToken ct)
    {
        Log.Info($"PricingService -> Opening connection (SubTotal of Order #{order.Id})...");
        await Task.Delay(200, ct);

        Log.Info($"PricingService -> Executing query for prices of Order #{order.Id}...");
        await Task.Delay(Random.Shared.Next(300, 601), ct);

        List<Product> products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct);

        decimal subTotal = 0m;
        foreach (OrderItem item in order.Items)
        {
            Product product = products.FirstOrDefault(p => p.Id == item.ProductId)
                ?? throw new InvalidOperationException($"Product #{item.ProductId} not found");

            item.UnitPrice = product.Price;
            subTotal += item.UnitPrice * item.Quantity;
        }

        subTotal = Round(subTotal);
        Log.Success($"PricingService -> Order #{order.Id} SubTotal = {subTotal:F2}");
        return subTotal;
    }

    // Discount API: needs order.SubTotal to be filled in already.
    public async Task<decimal> GetDiscountAsync(Order order, CancellationToken ct)
    {
        Log.Info($"PricingService -> Discount API: DNS/handshake (Order #{order.Id})...");
        await Task.Delay(150, ct);

        Log.Info($"PricingService -> Discount API: sending request (Order #{order.Id})...");
        await Task.Delay(300, ct);

        Log.Info($"PricingService -> Discount API: waiting for response (Order #{order.Id})...");
        await Task.Delay(Random.Shared.Next(500, 1501), ct);

        Customer customer = await _customers.GetByIdAsync(order.CustomerId, ct)
            ?? throw new InvalidOperationException($"Customer #{order.CustomerId} not found");

        Coupon? coupon = order.CouponCode is null
            ? null
            : await _coupons.GetByCodeAsync(order.CouponCode, ct);

        if (order.CouponCode is not null)
        {
            if (coupon is null)
                throw new InvalidCouponException($"Coupon '{order.CouponCode}' does not exist");

            if (coupon.ExpiryDate < DateTime.Today)
                throw new InvalidCouponException($"Coupon '{order.CouponCode}' has expired");
        }

        int loyaltyPercent = customer.LoyaltyTier switch
        {
            LoyaltyTier.Silver => 5,
            LoyaltyTier.Gold => 10,
            _ => 0
        };

        int percent = Math.Min(loyaltyPercent + (coupon?.DiscountPercent ?? 0), 30);
        decimal discount = Round(order.SubTotal * percent / 100m);

        Log.Success($"PricingService -> Order #{order.Id} Discount = {discount:F2} ({percent}%)");
        return discount;
    }

    // Shipping API: needs order.SubTotal to be filled in already.
    public async Task<decimal> CalculateShippingCostAsync(Order order, CancellationToken ct)
    {
        Log.Info($"PricingService -> Shipping API: DNS/handshake (Order #{order.Id})...");
        await Task.Delay(150, ct);

        // Injected failure: throws right after the handshake step.
        if (_failShippingForOrderIds.Contains(order.Id))
        {
            Log.Error($"PricingService -> Shipping API failed for Order #{order.Id}");
            throw new ShippingProviderUnavailableException("Shipping provider unavailable");
        }

        Log.Info($"PricingService -> Shipping API: sending request (Order #{order.Id})...");
        await Task.Delay(300, ct);

        Log.Info($"PricingService -> Shipping API: waiting for response (Order #{order.Id})...");
        await Task.Delay(Random.Shared.Next(500, 1501), ct);

        List<Product> products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct);

        int totalQuantity = order.Items.Sum(i => i.Quantity);
        decimal totalWeight = order.Items.Sum(i =>
            i.Quantity * (products.FirstOrDefault(p => p.Id == i.ProductId)?.WeightKg ?? 0m));

        decimal shipping = order.SubTotal >= 1000m
            ? 0m
            : Round(5.00m + 1.50m * totalQuantity + 2.00m * totalWeight);

        Log.Success($"PricingService -> Order #{order.Id} ShippingCost = {shipping:F2}");
        return shipping;
    }


    // 1 query + 2 concurrent APIs. Fills every money field of the order.
    public async Task<decimal> CalculateTotalAsync(Order order, CancellationToken ct)
    {
        order.SubTotal = await CalculateSubTotalAsync(order, ct);

        Task<decimal> discountTask = GetDiscountAsync(order, ct);
        Task<decimal> shippingTask = CalculateShippingCostAsync(order, ct);

        order.Discount = await discountTask;
        order.ShippingCost = await shippingTask;
        order.TotalAmount = Round(order.SubTotal - order.Discount + order.ShippingCost);

        return order.TotalAmount;
    }


    public static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
