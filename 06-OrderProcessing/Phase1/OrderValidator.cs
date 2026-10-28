using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;

namespace _06_OrderProcessing.Phase1;

public sealed class OrderValidator
{
    private readonly CustomerRepository _customers;
    private readonly ProductRepository _products;
    private readonly CouponRepository _coupons;

    public OrderValidator(
        CustomerRepository customers,
        ProductRepository products,
        CouponRepository coupons)
    {
        _customers = customers;
        _products = products;
        _coupons = coupons;
    }

    public async Task ValidateOrderAsync(Order order, CancellationToken ct)
    {
        string? failure = await FindFirstFailureAsync(order, ct);

        if (failure is null)
        {
            order.Status = OrderStatus.Validated;
            order.FailureReason = null;
            Log.Success($"OrderValidator -> Order #{order.Id} validated");
        }
        else
        {
            order.Status = OrderStatus.Rejected;
            order.FailureReason = failure;
            Log.Error($"OrderValidator -> Order #{order.Id} rejected: {failure}");
        }


    }

    // Runs the checks in the exact order needed and stops at the first failure
    // Returns the FailureReason or null when every check passed
    private async Task<string?> FindFirstFailureAsync(Order order, CancellationToken ct)
    {
        // 1. Customer exists
        Customer? customer = await _customers.GetByIdAsync(order.CustomerId, ct);
        if (customer is null)
            return $"Customer #{order.CustomerId} not found";

        // 2. Customer is active
        if (!customer.IsActive)
            return "Customer is inactive";

        // 3. Every product exists (all items are checked before any stock check)
        var products = new List<Product>();
        foreach (OrderItem item in order.Items)
        {
            Product? product = await _products.GetByIdAsync(item.ProductId, ct);
            if (product is null)
                return $"Product #{item.ProductId} not found";
            products.Add(product);
        }

        // 4. StockQuantity >= Quantity (checked only, NOT reserved)
        for (int i = 0; i < order.Items.Count; i++)
        {
            if (products[i].StockQuantity < order.Items[i].Quantity)
                return $"Insufficient stock for '{products[i].Name}'";
        }

        if (order.CouponCode is not null)
        {
            // 5. Coupon exists
            Coupon? coupon = await _coupons.GetByCodeAsync(order.CouponCode, ct);
            if (coupon is null)
                return $"Coupon '{order.CouponCode}' does not exist";

            // 6. Coupon ExpiryDate >= DateTime.Today
            if (coupon.ExpiryDate < DateTime.Today)
                return $"Coupon '{order.CouponCode}' has expired";
        }

        return null;
    }
}
