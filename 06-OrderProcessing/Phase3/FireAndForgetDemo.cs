using _06_OrderProcessing.Phase1;
using _06_OrderProcessing.Shared.Exceptions;
using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;

namespace _06_OrderProcessing.Phase3;

public sealed class FireAndForgetDemo
{
    private readonly OrderRepository _orderRepository;
    private readonly IEmailService _emailService;

    public FireAndForgetDemo(OrderRepository orderRepository, IEmailService emailService)
    {
        _orderRepository = orderRepository;
        _emailService = emailService;
    }

    // Broken on purpose (9.7). The CS4014 warning is the compiler telling us the call is not awaited
#pragma warning disable CS4014
    public async Task ProcessOrderBrokenAsync(Order order, Customer customer, CancellationToken ct)
    {
        await _orderRepository.SaveAsync(order, ct);
        _emailService.SendConfirmationAsync(order, customer, ct);   // not awaited
        Console.WriteLine("Order processing finished");
    }
#pragma warning restore CS4014

    // The fix: await the email, so the method finishes only when the email has
    // and a failure reaches the caller which decides what to do with it.
    public async Task ProcessOrderFixedAsync(Order order, Customer customer, CancellationToken ct)
    {
        await _orderRepository.SaveAsync(order, ct);

        try
        {
            await _emailService.SendConfirmationAsync(order, customer, ct);
        }
        catch (SmtpUnavailableException ex)
        {
            Log.Error($"FireAndForgetDemo -> Email for Order #{order.Id} failed: {ex.Message}");
        }

        Console.WriteLine("Order processing finished");
    }
}