using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;

namespace _06_OrderProcessing.Phase3;

public class SyncEmailService
{
    private readonly JsonFileStore _store;

    public SyncEmailService(JsonFileStore store) => _store = store;

    public void SendConfirmation(Order order, Customer customer)
    {
        Log.Info($"SyncEmailService -> Rendering template for Order #{order.Id}...");
        Thread.Sleep(200);
        Log.Info($"SyncEmailService -> Connecting to SMTP for Order #{order.Id}...");
        Thread.Sleep(300);
        Log.Info($"SyncEmailService -> Sending email for Order #{order.Id}...");
        Thread.Sleep(500);

        _store.Update<EmailMessage, bool>(DataFiles.Emails, emails =>
        {
            emails.Add(new EmailMessage
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                To = customer.Email,
                Subject = $"Order #{order.Id} confirmed",
                Body = $"Hello {customer.Name}, your order #{order.Id} total {order.TotalAmount:F2} is confirmed.",
                SentAt = DateTime.Now,
                Attempts = 1,
                LastError = null
            });
            return true;
        });

        Log.Success($"SyncEmailService -> Email for Order #{order.Id} sent");
    }
}
