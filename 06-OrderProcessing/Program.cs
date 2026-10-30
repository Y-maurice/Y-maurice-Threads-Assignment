using _06_OrderProcessing.Phase3;

public static class Program
{
    public static async Task Main(string[] args)
    {
        //await Phase1Runner.RunAsync(CancellationToken.None);

        //await Phase2Runner.RunAsync(CancellationToken.None);

        await Phase3Runner.RunAsync(CancellationToken.None);
    }
}