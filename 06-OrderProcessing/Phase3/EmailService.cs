using _06_OrderProcessing.Shared.Exceptions;
using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;
using System.Collections.Concurrent;

namespace _06_OrderProcessing.Phase3;

public sealed class EmailService : IEmailService
{
    private readonly JsonFileStore _store;
    private readonly IReadOnlyDictionary<int, int> _failAttempts;
    private readonly ConcurrentDictionary<int, int> _attempts = new();

    // failAttempts: order id -> how many attempts fail (int.MaxValue = always fails).
    public EmailService(JsonFileStore store, IReadOnlyDictionary<int, int>? failAttempts = null)
    {
        _store = store;
        _failAttempts = failAttempts ?? new Dictionary<int, int>();
    }

    // Email (SMTP): render template 200 -> connect to SMTP 300 -> send 500
    public async Task SendConfirmationAsync(Order order, Customer customer, CancellationToken ct)
    {
        int attempt = _attempts.AddOrUpdate(order.Id, 1, (_, n) => n + 1);

        Log.Info($"EmailService -> Rendering template for Order #{order.Id} (attempt {attempt})...");
        await Task.Delay(200, ct);

        Log.Info($"EmailService -> Connecting to SMTP for Order #{order.Id}...");
        await Task.Delay(300, ct);

        // Injected failure: the connection fails on the first N attempts.
        if (_failAttempts.TryGetValue(order.Id, out int failCount) && attempt <= failCount)
            throw new SmtpUnavailableException("SMTP server unavailable");

        Log.Info($"EmailService -> Sending email for Order #{order.Id}...");
        await Task.Delay(500, ct);

        var message = new EmailMessage
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            To = customer.Email,
            Subject = $"Order #{order.Id} confirmed",
            Body = $"Hello {customer.Name}, your order #{order.Id} total {order.TotalAmount:F2} is confirmed.",
            SentAt = DateTime.Now,
            Attempts = attempt,
            LastError = null
        };

        await _store.UpdateAsync<EmailMessage, bool>(DataFiles.Emails, emails =>
        {
            emails.Add(message);
            return true;
        }, ct);

        Log.Success($"EmailService -> Email for Order #{order.Id} sent (attempt {attempt})");
    }

    // 1 initial attempt + up to 3 retries, waiting 500, 1000, 2000 ms before each retry.
    // A failing email never throws: it is queued in email_retry.json.
    public async Task SendConfirmationWithRetryAsync(Order order, Customer customer, CancellationToken ct)
    {
        int[] waits = { 500, 1000, 2000 };
        string lastError = string.Empty;

        for (int retry = 0; retry <= waits.Length; retry++)
        {
            if (retry > 0)
            {
                Log.Info($"EmailService -> Retry {retry} for Order #{order.Id} in {waits[retry - 1]} ms...");
                await Task.Delay(waits[retry - 1], ct);
            }

            try
            {
                await SendConfirmationAsync(order, customer, ct);
                return;
            }
            catch (SmtpUnavailableException ex)
            {
                lastError = ex.Message;
                Log.Error($"EmailService -> Attempt {retry + 1} for Order #{order.Id} failed: {ex.Message}");
            }
        }

        // All 4 attempts failed.
        var queued = new EmailMessage
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            To = customer.Email,
            Subject = $"Order #{order.Id} confirmed",
            Body = $"Hello {customer.Name}, your order #{order.Id} total {order.TotalAmount:F2} is confirmed.",
            SentAt = DateTime.Now,
            Attempts = waits.Length + 1,
            LastError = lastError
        };

        await _store.UpdateAsync<EmailMessage, bool>(DataFiles.EmailRetry, retries =>
        {
            retries.Add(queued);
            return true;
        }, ct);

        Log.Error($"EmailService -> Email for Order #{order.Id} queued for retry later");
    }
}
