using System.Collections.Concurrent;

namespace ViewsAndEmails;

public sealed class AnalyticsAndEmailJobs
{
    public readonly ConcurrentDictionary<string, int> Views = new();
    public readonly ConcurrentQueue<string> EmailJobs = new();
    public readonly List<string> SentEmails = new();

    readonly object _sentSync = new();
    public volatile bool ProducersDone;

    public void RecordView(string product)
    {
        Views.AddOrUpdate(product, 1, (_, current) => current + 1);
    }

    public void PlaceOrder(int orderId)
    {
        EmailJobs.Enqueue($"Email: order {orderId}");
    }

    public void EmailWorker()
    {
        while (true)
        {
            bool producersFinished = ProducersDone;

            if (EmailJobs.TryDequeue(out string? job))
            {
                lock (_sentSync)
                    SentEmails.Add(job);
                continue;
            }

            if (producersFinished)
                return;

            Thread.Sleep(1);
        }
    }
}