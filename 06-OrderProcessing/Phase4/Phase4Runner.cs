using _06_OrderProcessing.Phase1;
using _06_OrderProcessing.Phase3;
using _06_OrderProcessing.Shared.Exceptions;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace _06_OrderProcessing.Phase4;

public static class Phase4Runner
{
    public static async Task RunAsync(CancellationToken ct)
    {
        var store = new JsonFileStore();
        List<Order> checkoutOrders = new();

        try
        {
            checkoutOrders = await RunLiveCheckoutAsync(store, ct);

            if (ct.IsCancellationRequested)
            {
                PrintCancelled(checkoutOrders);
                return;
            }

            await RunDashboardAsync(store, ct);
            await RunShippingAsync(store, ct);
            await RunFraudScoringAsync(ct);
            await RunEmailResendAsync(ct);
        }
        catch (OperationCanceledException)
        {
            PrintCancelled(checkoutOrders);
        }
    }

    private static void PrintCancelled(List<Order> orders)
    {
        int cancelled = orders.Count(o => o.Status == OrderStatus.Pending && o.FailureReason == "Cancelled");
        Console.WriteLine($"Run cancelled — {orders.Count - cancelled} orders finished, {cancelled} cancelled");
    }

    // ---------- Task 10.1: live checkout progress ----------
    private static async Task<List<Order>> RunLiveCheckoutAsync(JsonFileStore store, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("===== Task 10.1: live checkout progress =====");

        await DataSeeder.ResetAsync(ct);
        OrderProcessor processor = Phase3Runner.BuildProcessor(store, withFailures: true);
        List<Order> orders = await new IncomingOrderRepository(store).GetAllAsync(ct);

        var watch = Stopwatch.StartNew();

        // R1: start all 20 orders at once.
        List<Task<Order>> tasks = orders.Select(o => ProcessAsync(processor, o, ct)).ToList();

        // R2-R4: Task.WhenEach hands out each task the moment it finishes, in finishing order,
        // without a list that is searched or rebuilt after every completion.
        int done = 0;
        await foreach (Task<Order> finished in Task.WhenEach(tasks))
        {
            Order order = await finished;
            done++;

            string reason = order.FailureReason is null ? string.Empty : $" ({order.FailureReason})";
            Console.WriteLine($"[{done,2}/{orders.Count}] Order #{order.Id} -> {order.Status}{reason} after {watch.Elapsed.TotalSeconds:F1} s");
        }
        watch.Stop();

        // R5: count per status.
        Console.WriteLine(string.Join(Environment.NewLine,
            orders.GroupBy(o => o.Status).OrderBy(g => g.Key).Select(g => $"  {g.Key}: {g.Count()}")));

        return orders;
    }

    private static async Task<Order> ProcessAsync(OrderProcessor processor, Order order, CancellationToken ct)
    {
        await processor.ProcessOrderAsync(order, ct);
        return order;
    }

    // ---------- Task 10.2: customer dashboard ----------
    private static async Task RunDashboardAsync(JsonFileStore store, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("===== Task 10.2: customer dashboard =====");

        var service = new CustomerDashboardService(
            store, new CustomerRepository(store), new OrderRepository(store),
            failPaymentsForCustomerIds: new[] { 99 });

        foreach (int customerId in new[] { 3, 99 })
        {
            var watch = Stopwatch.StartNew();
            try
            {
                CustomerDashboard d = await service.LoadAsync(customerId, ct);
                watch.Stop();

                Console.WriteLine(string.Join(Environment.NewLine,
                    $"Customer #{customerId}: {d.Customer.Name} — " +
                    $"{d.Orders.Count} orders ({string.Join(", ", d.Orders.Select(o => $"#{o.Id} {o.Status}"))}), " +
                    $"{d.Payments.Count} payments ({string.Join(", ", d.Payments.Select(p => $"#{p.OrderId} {p.Status}"))}), " +
                    $"{d.Emails.Count} emails ({string.Join(", ", d.Emails.Select(e => $"#{e.OrderId}"))})",
                    $"Loaded in {watch.Elapsed.TotalSeconds:F1} s"));
            }
            catch (AggregateException ex)
            {
                watch.Stop();
                Console.WriteLine(string.Join(Environment.NewLine,
                    new[] { $"Customer #{customerId}: {ex.InnerExceptions.Count} failures after {watch.Elapsed.TotalSeconds:F1} s" }
                        .Concat(ex.InnerExceptions.Select(inner => $"  - {inner.Message}"))));
            }
        }
    }

    // ---------- Task 10.3: fastest shipping quote ----------
    private static async Task RunShippingAsync(JsonFileStore store, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("===== Task 10.3: fastest shipping quote =====");

        List<Order> all = await new IncomingOrderRepository(store).GetAllAsync(ct);

        IShippingCarrier[] Carriers(params int[] failFor) =>
            [new FastShip(store, failFor), new CairoExpress(store, failFor), new NileCourier(store, failFor)];

        // R7: orders #1 to #5.
        var service = new ShippingQuoteService(Carriers());
        foreach (int id in new[] { 1, 2, 3, 4, 5 })
        {
            ShippingQuote quote = await service.GetFastestQuoteAsync(all.First(o => o.Id == id), ct);
            Console.WriteLine(Describe(id, quote));
        }

        // R5: every carrier fails for order #6.
        try
        {
            await new ShippingQuoteService(Carriers(6)).GetFastestQuoteAsync(all.First(o => o.Id == 6), ct);
        }
        catch (ShippingProviderUnavailableException ex)
        {
            Console.WriteLine($"Order #6 -> {ex.Message}");
        }

        // R4: a budget that is too short gives the flat rate.
        var tight = new ShippingQuoteService(Carriers(), TimeSpan.FromMilliseconds(300));
        Console.WriteLine(Describe(1, await tight.GetFastestQuoteAsync(all.First(o => o.Id == 1), ct)) + "  (300 ms budget)");

        // R6: cancelling the caller's token cancels every carrier.
        using var shortRun = CancellationTokenSource.CreateLinkedTokenSource(ct);
        shortRun.CancelAfter(200);
        try
        {
            await service.GetFastestQuoteAsync(all.First(o => o.Id == 1), shortRun.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Order #1 -> the caller cancelled after 200 ms: every carrier was cancelled");
        }
        ct.ThrowIfCancellationRequested();
    }

    private static string Describe(int orderId, ShippingQuote q) =>
        $"Order #{orderId} -> {q.Carrier}, price {q.Price:F2}, {q.ElapsedMs} ms, " +
        $"cancelled: {(q.Cancelled is { Count: > 0 } ? string.Join(", ", q.Cancelled) : "none")}";

    // ---------- Task 10.4 A: fraud scoring (CPU-bound) ----------
    private static async Task RunFraudScoringAsync(CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("===== Task 10.4 A: fraud scoring =====");

        List<int> ids = Enumerable.Range(1, 10_000).ToList();

        var sequentialWatch = Stopwatch.StartNew();
        List<int> sequential = FraudScoring.FindFlaggedSequential(ids, ct);
        sequentialWatch.Stop();

        var threads = new ConcurrentDictionary<int, byte>();
        var parallelWatch = Stopwatch.StartNew();
        List<int> parallel = await Task.Run(() => FraudScoring.FindFlaggedParallel(ids, threads, ct), ct);
        parallelWatch.Stop();

        double speedUp = sequentialWatch.Elapsed.TotalSeconds / parallelWatch.Elapsed.TotalSeconds;
        Console.WriteLine(string.Join(Environment.NewLine,
            $"Flagged: {parallel.Count} (first five: {string.Join(", ", parallel.Take(5))}), " +
            $"sequential found {sequential.Count}, same ids: {sequential.SequenceEqual(parallel)}",
            $"Sequential: {sequentialWatch.Elapsed.TotalSeconds:F2} s",
            $"Parallel:   {parallelWatch.Elapsed.TotalSeconds:F2} s (speed-up x{speedUp:F1})",
            $"Distinct threads used by the parallel run: {threads.Count}, Environment.ProcessorCount: {Environment.ProcessorCount}"));
    }

    // ---------- Task 10.4 B: email re-send (I/O-bound) ----------
    private static async Task RunEmailResendAsync(CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("===== Task 10.4 B: email re-send, at most 4 at a time =====");

        // A separate folder, so the files of Task 10.1 in Data/ stay as they are.
        string folder = Path.Combine(AppContext.BaseDirectory, "Data-resend");
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);

        var resendStore = new JsonFileStore(folder);
        var email = new EmailService(resendStore);   // no injected failures

        var customer = new Customer { Id = 1, Name = "Resend Test", Email = "resend.test@example.com", IsActive = true };
        List<Order> orders = Enumerable.Range(1001, 30)
            .Select(id => new Order { Id = id, CustomerId = 1, TotalAmount = 10m, Status = OrderStatus.Completed })
            .ToList();

        var meter = new ConcurrencyMeter();
        var watch = Stopwatch.StartNew();

        // The library limits the concurrency to 4: no counter or semaphore of mine does that.
        await Parallel.ForEachAsync(
            orders,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            async (order, token) =>
            {
                meter.Enter();
                try
                {
                    await email.SendConfirmationAsync(order, customer, token);
                }
                finally
                {
                    meter.Exit();
                }
            });
        watch.Stop();

        List<EmailMessage> saved = await resendStore.ReadAllAsync<EmailMessage>(DataFiles.Emails, ct);
        Console.WriteLine(string.Join(Environment.NewLine,
            $"Highest number of sends running at the same time: {meter.Max}",
            $"emails.json (Data-resend) contains {saved.Count} records, {saved.Select(e => e.OrderId).Distinct().Count()} distinct orders",
            $"Total time: {watch.Elapsed.TotalSeconds:F1} s"));
    }

    // Measurement only: it observes the concurrency, it does not limit it.
    private sealed class ConcurrencyMeter
    {
        private readonly object _gate = new();
        private int _running;

        public int Max { get; private set; }

        public void Enter()
        {
            lock (_gate)
            {
                _running++;
                if (_running > Max)
                    Max = _running;
            }
        }

        public void Exit()
        {
            lock (_gate)
                _running--;
        }
    }
}
