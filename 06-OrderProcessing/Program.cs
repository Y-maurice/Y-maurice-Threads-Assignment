using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Storage;

public static class Program
{
    public static async Task Main(string[] args)
    {
        await DataSeeder.ResetAsync(CancellationToken.None);

        Log.Info("CustomerRepository -> Opening connection...");
        Log.Success("CustomerRepository -> Customer #3 found (Omar Hassan)");
        Log.Error("PaymentGateway -> Card declined");

    }
}