using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;

namespace _06_OrderProcessing.Phase1;

public class SyncOrderValidator
{
    private readonly SyncCustomerRepository _customers;
    private readonly SyncProductRepository _products;
    private readonly SyncCouponRepository _coupons;

    public SyncOrderValidator(
        SyncCustomerRepository customers,
        SyncProductRepository products,
        SyncCouponRepository coupons)
    {
        _customers = customers;
        _products = products;
        _coupons = coupons;
    }


    public void ValidateOrder(Order order)
    {
        string? failure = FindFirstFailure(order);

        if (failure is null)
        {
            order.Status = OrderStatus.Validated;
            order.FailureReason = null;
            Log.Success($"SyncOrderValidator -> Order #{order.Id} validated");
        }
        else
        {
            order.Status = OrderStatus.Rejected;
            order.FailureReason = failure;
            Log.Error($"SyncOrderValidator -> Order #{order.Id} rejected: {failure}");
        }
    }

    private string? FindFirstFailure(Order order)
    {
        Customer? customer = _customers.GetById(order.CustomerId);
        if (customer is null)
            return $"Customer #{order.CustomerId} not found";

        if (!customer.IsActive)
            return "Customer is inactive";

        var products = new List<Product>();
        foreach (OrderItem item in order.Items)
        {
            Product? product = _products.GetById(item.ProductId);
            if (product is null)
                return $"Product #{item.ProductId} not found";
            products.Add(product);
        }

        for (int i = 0; i < order.Items.Count; i++)
        {
            if (products[i].StockQuantity < order.Items[i].Quantity)
                return $"Insufficient stock for '{products[i].Name}'";
        }

        if (order.CouponCode is not null)
        {
            Coupon? coupon = _coupons.GetByCode(order.CouponCode);
            if (coupon is null)
                return $"Coupon '{order.CouponCode}' does not exist";

            if (coupon.ExpiryDate < DateTime.Today)
                return $"Coupon '{order.CouponCode}' has expired";
        }

        return null;
    }
}
