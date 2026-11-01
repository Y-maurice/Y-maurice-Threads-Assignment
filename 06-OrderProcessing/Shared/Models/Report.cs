namespace _06_OrderProcessing.Shared.Models;

public class Report
{
    public int TotalOrders { get; set; }
    public decimal TotalRevenue { get; set; }
    public int CompletedCount { get; set; }
    public int RejectedCount { get; set; }
    public int FailedCount { get; set; }
    public int PaymentFailedCount { get; set; }
    public string? TopSellingProduct { get; set; }
    public int TopSellingQuantity { get; set; }
    public decimal AverageOrderValue { get; set; }
    public DateTime GeneratedAt { get; set; }
}