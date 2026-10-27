namespace _06_OrderProcessing.Shared.Models;

public class Coupon
{
    public string Code { get; set; } = string.Empty;
    public int DiscountPercent { get; set; }
    public DateTime ExpiryDate { get; set; }
}
