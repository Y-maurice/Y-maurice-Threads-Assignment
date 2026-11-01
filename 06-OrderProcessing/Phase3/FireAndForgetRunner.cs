using _06_OrderProcessing.Phase1;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;

namespace _06_OrderProcessing.Phase3;

public static class FireAndForgetRunner
{
    public static async Task RunAsync(CancellationToken ct)
    {
        // Best-effort: shows an exception nobody awaited, but only when the GC finalizes the task.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.WriteLine($"[UnobservedTaskException] {e.Exception.InnerException?.Message}");
            e.SetObserved();
        };

        var store = new JsonFileStore();

        // ---- Case 1: BROKEN, the email works. The method returns before the email is done. ----
        Console.WriteLine();
        Console.WriteLine("===== 1. BROKEN: method returns while the email is still being sent =====");
        (Order order, Customer customer) = await PrepareAsync(store, ct);
        var broken = new FireAndForgetDemo(new OrderRepository(store), new EmailService(store));

        await broken.ProcessOrderBrokenAsync(order, customer, ct);
        Console.WriteLine($"Emails in emails.json right after the method returned: {await CountEmailsAsync(store, ct)}");
        await Task.Delay(1500, ct);
        Console.WriteLine($"Emails in emails.json 1.5 s later: {await CountEmailsAsync(store, ct)}");

        // ---- Case 2: BROKEN, the email throws. Nobody hears about it. ----
        Console.WriteLine();
        Console.WriteLine("===== 2. BROKEN: the email throws =====");
        (order, customer) = await PrepareAsync(store, ct);
        var brokenFailing = new FireAndForgetDemo(
            new OrderRepository(store),
            new EmailService(store, new Dictionary<int, int> { [1] = int.MaxValue }));

        await brokenFailing.ProcessOrderBrokenAsync(order, customer, ct);
        await Task.Delay(1500, ct);   // the email fails during this wait
        Console.WriteLine("The method reported success and no error was raised to the caller.");
        Console.WriteLine($"Emails in emails.json: {await CountEmailsAsync(store, ct)}");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Task.Delay(200, ct);

        // ---- Case 3: FIXED, the email works. ----
        Console.WriteLine();
        Console.WriteLine("===== 3. FIXED: the email is awaited =====");
        (order, customer) = await PrepareAsync(store, ct);
        var fixedDemo = new FireAndForgetDemo(new OrderRepository(store), new EmailService(store));

        await fixedDemo.ProcessOrderFixedAsync(order, customer, ct);
        Console.WriteLine($"Emails in emails.json right after the method returned: {await CountEmailsAsync(store, ct)}");

        // ---- Case 4: FIXED, the email throws. The failure is seen and handled. ----
        Console.WriteLine();
        Console.WriteLine("===== 4. FIXED: the email throws =====");
        (order, customer) = await PrepareAsync(store, ct);
        var fixedFailing = new FireAndForgetDemo(
            new OrderRepository(store),
            new EmailService(store, new Dictionary<int, int> { [1] = int.MaxValue }));

        await fixedFailing.ProcessOrderFixedAsync(order, customer, ct);
    }

    // The app exits before the email finishes. Run it ALONE from Program.cs: the process ends right after.
    public static async Task RunExitDemoAsync(CancellationToken ct)
    {
        var store = new JsonFileStore();
        (Order order, Customer customer) = await PrepareAsync(store, ct);
        var broken = new FireAndForgetDemo(new OrderRepository(store), new EmailService(store));

        await broken.ProcessOrderBrokenAsync(order, customer, ct);
        Console.WriteLine("Main is returning now: the console app exits.");
    }

    private static async Task<(Order, Customer)> PrepareAsync(JsonFileStore store, CancellationToken ct)
    {
        await DataSeeder.ResetAsync(ct);

        List<Order> all = await new IncomingOrderRepository(store).GetAllAsync(ct);
        Order order = all.First(o => o.Id == 1);
        Customer customer = (await new CustomerRepository(store).GetByIdAsync(order.CustomerId, ct))!;
        return (order, customer);
    }

    private static async Task<int> CountEmailsAsync(JsonFileStore store, CancellationToken ct) =>
        (await store.ReadAllAsync<EmailMessage>(DataFiles.Emails, ct)).Count;
}
