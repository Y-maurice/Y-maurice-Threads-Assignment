namespace _06_OrderProcessing.Shared.Models;

public class PaymentTransaction
{
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
    public string? TransactionId { get; set; }
    public DateTime CreatedAt { get; set; }
}
