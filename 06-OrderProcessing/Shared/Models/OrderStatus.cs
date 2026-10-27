namespace _06_OrderProcessing.Shared.Models;

public enum OrderStatus
{
    Pending,
    Validated,
    Rejected,
    PaymentFailed,
    Completed,
    Failed
}
