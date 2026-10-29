using _06_OrderProcessing.Phase2;

public static class Program
{
    public static async Task Main(string[] args)
    {
        //await Phase1Runner.RunAsync(CancellationToken.None);

        await Phase2Runner.RunAsync(CancellationToken.None);

    }
}