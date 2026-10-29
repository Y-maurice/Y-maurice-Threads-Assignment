using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;

namespace _06_OrderProcessing.Phase2;

public class InvoiceGenerator
{
    public async Task GenerateAsync(Order order, CancellationToken ct)
    {
        Log.Info($"InvoiceGenerator -> Rendering invoice for Order #{order.Id}...");

        // CPU-bound step: Task.Run moves the loop to a thread-pool thread
        // so the calling method is not stuck running it
        long checksum = await Task.Run(() =>
        {
            long h = order.Id;
            for (int i = 0; i < 20_000_000; i++)
            {
                h = (h * 31 + i) % 1_000_000_007;
                if (i % 1_000_000 == 0)
                    ct.ThrowIfCancellationRequested();
            }
            return h;
        }, ct);

        // Invoice files are one of the two allowed exceptions to "only JsonFileStore writes files"
        string folder = Path.Combine(DataFiles.DataDirectory, "invoices");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"invoice_{order.Id}.txt");

        string text =
            $"""
            SwiftBite Invoice
            Order:     {order.Id}
            Customer:  {order.CustomerId}
            SubTotal:  {order.SubTotal:F2}
            Discount:  {order.Discount:F2}
            Shipping:  {order.ShippingCost:F2}
            Total:     {order.TotalAmount:F2}
            Checksum:  {checksum}
            """;

        await File.WriteAllTextAsync(path, text, ct);
        Log.Success($"InvoiceGenerator -> Invoice written: invoice_{order.Id}.txt");
    }
}
