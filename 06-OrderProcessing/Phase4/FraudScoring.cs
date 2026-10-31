using System.Collections.Concurrent;

namespace _06_OrderProcessing.Phase4;

public static class FraudScoring
{
    // CPU-bound
    public static int Score(int orderId)
    {
        long h = orderId;
        for (int i = 0; i < 100_000; i++)
            h = (h * 31 + i) % 1_000_000_007;
        return (int)(h % 100);
    }

    // R1: a plain sequential loop
    public static List<int> FindFlaggedSequential(IReadOnlyList<int> orderIds, CancellationToken ct)
    {
        var flagged = new List<int>();
        foreach (int id in orderIds)
        {
            ct.ThrowIfCancellationRequested();
            if (Score(id) >= 90)
                flagged.Add(id);
        }
        return flagged;
    }

    // R2-R4: the library spreads the work over the CPU cores never more than ProcessorCount at once
    // ConcurrentBag collects the ids without losing or duplicating any
    public static List<int> FindFlaggedParallel(
        IReadOnlyList<int> orderIds, ConcurrentDictionary<int, byte> threadsUsed, CancellationToken ct)
    {
        var flagged = new ConcurrentBag<int>();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = ct
        };

        Parallel.ForEach(orderIds, options, id =>
        {
            threadsUsed.TryAdd(Environment.CurrentManagedThreadId, 0);
            if (Score(id) >= 90)
                flagged.Add(id);
        });

        return flagged.OrderBy(id => id).ToList();
    }
}
