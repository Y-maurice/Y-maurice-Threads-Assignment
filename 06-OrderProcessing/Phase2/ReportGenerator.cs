using _06_OrderProcessing.Phase1;
using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;
using System.Text.Json;

namespace _06_OrderProcessing.Phase2;

public sealed class ReportGenerator
{
    private readonly OrderRepository _orders;
    private readonly ProductRepository _products;

    public ReportGenerator(OrderRepository orders, ProductRepository products)
    {
        _orders = orders;
        _products = products;
    }

    public async Task<Report> GenerateAsync(CancellationToken ct)
    {
        // Data is loaded through the repositories, so it has the simulated delays.
        List<Order> orders = await _orders.GetAllAsync(ct);
        List<Order> completed = orders.Where(o => o.Status == OrderStatus.Completed).ToList();

        // Top seller: highest total quantity over Completed orders, ties -> lowest ProductId.
        var top = completed
            .SelectMany(o => o.Items)
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity) })
            .OrderByDescending(x => x.Quantity)
            .ThenBy(x => x.ProductId)
            .FirstOrDefault();

        string? topName = null;
        if (top is not null)
        {
            Product? product = await _products.GetByIdAsync(top.ProductId, ct);
            topName = product?.Name;
        }

        decimal revenue = completed.Sum(o => o.TotalAmount);

        var report = new Report
        {
            TotalOrders = orders.Count,
            TotalRevenue = revenue,
            CompletedCount = completed.Count,
            RejectedCount = orders.Count(o => o.Status == OrderStatus.Rejected),
            FailedCount = orders.Count(o => o.Status == OrderStatus.Failed),
            PaymentFailedCount = orders.Count(o => o.Status == OrderStatus.PaymentFailed),
            TopSellingProduct = topName,
            TopSellingQuantity = top?.Quantity ?? 0,
            AverageOrderValue = completed.Count == 0
                ? 0m
                : PricingService.Round(revenue / completed.Count),
            GeneratedAt = DateTime.Now
        };

        // Data/reports/report_{yyyyMMdd_HHmmss}.json
        string folder = Path.Combine(DataFiles.DataDirectory, "reports");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"report_{report.GeneratedAt:yyyyMMdd_HHmmss}.json");

        string json = JsonSerializer.Serialize(report, JsonFileStore.Options);
        await File.WriteAllTextAsync(path, json, ct);

        Log.Success($"ReportGenerator -> Report written: {Path.GetFileName(path)}");
        return report;
    }
}
