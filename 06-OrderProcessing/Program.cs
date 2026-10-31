using _06_OrderProcessing.Phase1;
using _06_OrderProcessing.Phase2;
using _06_OrderProcessing.Phase3;
using System.Text;

public static class Program
{
    private sealed record Phase(string Name, Func<CancellationToken, Task> Run, bool InAll = true);

    private static readonly Phase[] Phases =
    [
        new("phase1", Phase1Runner.RunAsync),
        new("phase2", Phase2Runner.RunAsync),
        new("phase3", Phase3Runner.RunAsync),
        new("phase3-demos", RunPhase3DemosAsync, InAll: false),
        new("phase4", Phase4Runner.RunAsync),
    ];

    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;   // for the "—" in the cancelled line

        int? cancelAfterMs = ParseCancelAfter(args);
        string? mode = args.FirstOrDefault(a => !a.StartsWith("--") && !int.TryParse(a, out _))
                       ?? AskForModeFromMenu();

        if (mode is null)
        {
            Console.WriteLine("No valid choice.");
            return;
        }

        if (mode.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            foreach (Phase phase in Phases.Where(p => p.InAll))
            {
                if (await RunPhaseAsync(phase, cancelAfterMs))
                    break;   // a cancelled run ends the whole command
            }
            return;
        }

        Phase? selected = Phases.FirstOrDefault(p => p.Name.Equals(mode, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            Console.WriteLine($"Unknown mode '{mode}'. Use: {string.Join(" | ", Phases.Select(p => p.Name))} | all");
            return;
        }

        await RunPhaseAsync(selected, cancelAfterMs);
    }

    // Each phase run owns one cancellation source (6.6 R2). Returns true if the run was cancelled.
    private static async Task<bool> RunPhaseAsync(Phase phase, int? cancelAfterMs)
    {
        using var cts = new CancellationTokenSource();

        ConsoleCancelEventHandler onCancelKey = (_, e) =>
        {
            e.Cancel = true;   // do not kill the process: cancel the run instead
            Console.WriteLine("Ctrl+C pressed: cancelling the run...");
            cts.Cancel();
        };
        Console.CancelKeyPress += onCancelKey;

        if (cancelAfterMs is not null)
            cts.CancelAfter(cancelAfterMs.Value);

        try
        {
            await phase.Run(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Only reached if a runner did not handle the cancellation itself.
            Console.WriteLine($"{phase.Name} was cancelled.");
        }
        finally
        {
            Console.CancelKeyPress -= onCancelKey;
        }

        return cts.IsCancellationRequested;
    }

    private static async Task RunPhase3DemosAsync(CancellationToken ct)
    {
        await SyncVsAsyncComparison.RunAsync(ct);
        await FireAndForgetRunner.RunAsync(ct);
    }

    private static int? ParseCancelAfter(string[] args)
    {
        int index = Array.IndexOf(args, "--cancel-after");
        if (index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out int ms))
            return ms;
        return null;
    }

    private static string? AskForModeFromMenu()
    {
        Console.WriteLine("Order Processing System");
        Console.WriteLine("  1) Phase 1 - Async data access layer");
        Console.WriteLine("  2) Phase 2 - Concurrent processing");
        Console.WriteLine("  3) Phase 3 - Full checkout workflow");
        Console.WriteLine("  4) Phase 3 - Demos (sync vs async, fire-and-forget)");
        Console.WriteLine("  5) Phase 4 - Scale-up");
        Console.WriteLine("  6) All phases");
        Console.Write("Choose: ");

        return Console.ReadLine()?.Trim() switch
        {
            "1" => "phase1",
            "2" => "phase2",
            "3" => "phase3",
            "4" => "phase3-demos",
            "5" => "phase4",
            "6" => "all",
            _ => null
        };
    }
}