using _06_OrderProcessing.Phase1;
using _06_OrderProcessing.Shared.Exceptions;
using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;

namespace _06_OrderProcessing.Phase3;

public class SyncOrderProcessor
{
    private readonly SyncCustomerRepository _customers;
    private readonly SyncProductRepository _products;
    private readonly SyncPricingService _pricing;
    private readonly SyncPaymentGateway _gateway;
    private readonly SyncOrderRepository _orders;
    private readonly SyncEmailService _email;

    public SyncOrderProcessor(
        SyncCustomerRepository customers,
        SyncProductRepository products,
        SyncPricingService pricing,
        SyncPaymentGateway gateway,
        SyncOrderRepository orders,
        SyncEmailService email)
    {
        _customers = customers;
        _products = products;
        _pricing = pricing;
        _gateway = gateway;
        _orders = orders;
        _email = email;
    }

    public bool ProcessOrder(Order order)
    {
        Customer? customer = _customers.GetById(order.CustomerId);
        if (customer is null)
            return Stop(order, OrderStatus.Rejected, $"Customer #{order.CustomerId} not found");
        if (!customer.IsActive)
            return Stop(order, OrderStatus.Rejected, "Customer is inactive");

        if (!ReserveAll(order))
        {
            string? name = FindShortageName(order);
            return Stop(order, OrderStatus.Rejected, $"Insufficient stock for '{name}'");
        }

        try
        {
            _pricing.CalculateTotal(order);
        }
        catch (InvalidCouponException ex)
        {
            ReleaseAll(order);
            return Stop(order, OrderStatus.Rejected, ex.Message);
        }

        PaymentResult payment = _gateway.ProcessPayment(order.Id, order.TotalAmount);
        if (!payment.Success)
        {
            ReleaseAll(order);
            return Stop(order, OrderStatus.PaymentFailed, payment.Message ?? "Payment failed");
        }

        order.TransactionId = payment.TransactionId;
        order.Status = OrderStatus.Completed;
        order.FailureReason = null;
        _orders.Save(order);

        _email.SendConfirmation(order, customer);
        Log.Success($"SyncOrderProcessor -> Order #{order.Id} completed");
        return true;
    }

    private bool ReserveAll(Order order)
    {
        var reserved = new List<OrderItem>();
        foreach (OrderItem item in order.Items)
        {
            if (!_products.ReserveStock(item.ProductId, item.Quantity))
            {
                foreach (OrderItem done in reserved)
                    _products.ReleaseStock(done.ProductId, done.Quantity);
                return false;
            }
            reserved.Add(item);
        }
        return true;
    }

    private void ReleaseAll(Order order)
    {
        foreach (OrderItem item in order.Items)
            _products.ReleaseStock(item.ProductId, item.Quantity);
    }

    private string? FindShortageName(Order order)
    {
        foreach (OrderItem item in order.Items)
        {
            Product? product = _products.GetById(item.ProductId);
            if (product is not null && product.StockQuantity < item.Quantity)
                return product.Name;
        }
        return null;
    }

    private bool Stop(Order order, OrderStatus status, string reason)
    {
        order.Status = status;
        order.FailureReason = reason;
        Log.Error($"SyncOrderProcessor -> Order #{order.Id} {status}: {reason}");
        _orders.Save(order);
        return false;
    }
}
