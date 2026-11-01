namespace _06_OrderProcessing.Shared.Models;

public class EmailMessage
{
    public Guid Id { get; set; }
    public int OrderId { get; set; }
    public string To { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
