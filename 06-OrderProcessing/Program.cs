using _06_OrderProcessing.Phase1;

public static class Program
{
    public static async Task Main(string[] args)
    {
        await Phase1Runner.RunAsync(CancellationToken.None);


    }
}