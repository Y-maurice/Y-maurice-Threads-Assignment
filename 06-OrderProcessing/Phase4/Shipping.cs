using _06_OrderProcessing.Phase2;
using _06_OrderProcessing.Shared.Exceptions;
using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;
using System.Diagnostics;

namespace _06_OrderProcessing.Phase4;


public record ShippingQuote(
    string Carrier, decimal Price, long ElapsedMs, IReadOnlyList<string>? Cancelled = null);



public interface IShippingCarrier
{
    string Name { get; }
    Task<decimal> GetQuoteAsync(Order order, CancellationToken ct);
}


// Shared mechanics of the three carriers: handshake, a random wait, the price formula of 8.1 times a factor.
public abstract class ShippingCarrierBase : IShippingCarrier
{
    private readonly JsonFileStore _store;
    private readonly HashSet<int> _failForOrderIds;

    protected ShippingCarrierBase(JsonFileStore store, IEnumerable<int>? failForOrderIds)
    {
        _store = store;
        _failForOrderIds = (failForOrderIds ?? Array.Empty<int>()).ToHashSet();
    }

    public abstract string Name { get; }
    protected abstract decimal Factor { get; }
    protected abstract int MinMs { get; }
    protected abstract int MaxMs { get; }
    protected virtual bool FailsByDesign(Order order) => false;

    public async Task<decimal> GetQuoteAsync(Order order, CancellationToken ct)
    {
        try
        {
            Log.Info($"[{Name}] handshake for Order #{order.Id}...");
            await Task.Delay(150, ct);

            // A failing carrier fails right after the handshake.
            if (_failForOrderIds.Contains(order.Id) || FailsByDesign(order))
            {
                Log.Error($"[{Name}] failed for Order #{order.Id}");
                throw new ShippingProviderUnavailableException($"{Name} unavailable");
            }

            Log.Info($"[{Name}] waiting for the quote of Order #{order.Id}...");
            await Task.Delay(Random.Shared.Next(MinMs, MaxMs + 1), ct);

            decimal price = PricingService.Round(await BaseShippingAsync(order, ct) * Factor);
            Log.Success($"[{Name}] quote for Order #{order.Id}: {price:F2}");
            return price;
        }
        catch (OperationCanceledException)
        {
            Log.Info($"[{Name}] cancelled");
            throw;
        }
    }

    // 5.00 + 1.50 x quantity + 2.00 x weight, free from a SubTotal of 1000 (8.1).
    private async Task<decimal> BaseShippingAsync(Order order, CancellationToken ct)
    {
        List<Product> products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct);

        decimal subTotal = 0m, weight = 0m;
        int quantity = 0;
        foreach (OrderItem item in order.Items)
        {
            Product product = products.First(p => p.Id == item.ProductId);
            subTotal += product.Price * item.Quantity;
            weight += product.WeightKg * item.Quantity;
            quantity += item.Quantity;
        }

        return subTotal >= 1000m ? 0m : 5.00m + 1.50m * quantity + 2.00m * weight;
    }
}


public class FastShip : ShippingCarrierBase
{
    public FastShip(JsonFileStore store, IEnumerable<int>? failForOrderIds = null) : base(store, failForOrderIds) { }
    public override string Name => "FastShip";
    protected override decimal Factor => 1.00m;
    protected override int MinMs => 500;
    protected override int MaxMs => 1500;
}


public class CairoExpress : ShippingCarrierBase
{
    public CairoExpress(JsonFileStore store, IEnumerable<int>? failForOrderIds = null) : base(store, failForOrderIds) { }
    public override string Name => "CairoExpress";
    protected override decimal Factor => 0.95m;
    protected override int MinMs => 300;
    protected override int MaxMs => 2000;
    protected override bool FailsByDesign(Order order) => order.Id % 2 == 0;   // even order ids
}

public class NileCourier : ShippingCarrierBase
{
    public NileCourier(JsonFileStore store, IEnumerable<int>? failForOrderIds = null) : base(store, failForOrderIds) { }
    public override string Name => "NileCourier";
    protected override decimal Factor => 1.10m;
    protected override int MinMs => 800;
    protected override int MaxMs => 1200;
}


public class ShippingQuoteService
{
    private readonly IReadOnlyList<IShippingCarrier> _carriers;
    private readonly TimeSpan _budget;

    public ShippingQuoteService(IEnumerable<IShippingCarrier> carriers, TimeSpan? budget = null)
    {
        _carriers = carriers.ToList();
        _budget = budget ?? TimeSpan.FromMilliseconds(1200);
    }

    public async Task<ShippingQuote> GetFastestQuoteAsync(Order order, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();

        // One source for the whole race: the caller's token, the time budget and "someone won" all cancel it.
        using var race = CancellationTokenSource.CreateLinkedTokenSource(ct);
        race.CancelAfter(_budget);

        // Ask every carrier at the same time.
        var running = new Dictionary<Task<decimal>, IShippingCarrier>();
        foreach (IShippingCarrier carrier in _carriers)
            running.Add(carrier.GetQuoteAsync(order, race.Token), carrier);

        int failed = 0;
        while (running.Count > 0)
        {
            Task<decimal> finished = await Task.WhenAny(running.Keys);
            IShippingCarrier winner = running[finished];
            running.Remove(finished);

            if (finished.IsCompletedSuccessfully)
            {
                decimal price = await finished;
                long elapsed = watch.ElapsedMilliseconds;

                // A quote is chosen: cancel every carrier still running and note who they were.
                race.Cancel();
                var cancelled = new List<string>();
                foreach (var (task, other) in running)
                {
                    try { await task; }
                    catch (OperationCanceledException) { cancelled.Add(other.Name); }
                    catch (ShippingProviderUnavailableException) { }
                }

                return new ShippingQuote(winner.Name, price, elapsed, cancelled);
            }

            if (finished.IsFaulted)
            {
                failed++;
                _ = finished.Exception;   // the failure is observed: keep waiting for the next carrier
            }

            // A cancelled task (budget over, or the caller cancelled) just leaves the race.
        }

        ct.ThrowIfCancellationRequested();   // the caller cancelled: nothing to return

        if (failed == _carriers.Count)
            throw new ShippingProviderUnavailableException("All carriers failed");

        // The budget is over and no successful quote arrived.
        return new ShippingQuote("Flat rate", 25.00m, watch.ElapsedMilliseconds, new List<string>());
    }
}


