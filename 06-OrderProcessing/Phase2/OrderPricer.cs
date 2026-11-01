using _06_OrderProcessing.Shared.Models;

namespace _06_OrderProcessing.Phase2;

public class OrderPricer
{
    private readonly PricingService _pricing;

    public OrderPricer(PricingService pricing) => _pricing = pricing;

    // SubTotal first then discount and shipping at the SAME time.
    public async Task PriceOrderAsync(Order order, CancellationToken ct)
    {
        order.SubTotal = await _pricing.CalculateSubTotalAsync(order, ct);

        // Start both (no await yet) then await them one by one.
        Task<decimal> discountTask = _pricing.GetDiscountAsync(order, ct);
        Task<decimal> shippingTask = _pricing.CalculateShippingCostAsync(order, ct);

        order.Discount = await discountTask;
        order.ShippingCost = await shippingTask;

        order.TotalAmount = PricingService.Round(order.SubTotal - order.Discount + order.ShippingCost);
    }

    // Same work, but each call is awaited right after starting it.
    public async Task PriceOrderSequentialAsync(Order order, CancellationToken ct)
    {
        order.SubTotal = await _pricing.CalculateSubTotalAsync(order, ct);
        order.Discount = await _pricing.GetDiscountAsync(order, ct);
        order.ShippingCost = await _pricing.CalculateShippingCostAsync(order, ct);

        order.TotalAmount = PricingService.Round(order.SubTotal - order.Discount + order.ShippingCost);
    }
}
