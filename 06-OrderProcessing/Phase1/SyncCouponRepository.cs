using _06_OrderProcessing.Shared.Logging;
using _06_OrderProcessing.Shared.Models;
using _06_OrderProcessing.Shared.Storage;

namespace _06_OrderProcessing.Phase1;

public class SyncCouponRepository
{
    private readonly JsonFileStore _store;

    public SyncCouponRepository(JsonFileStore store) => _store = store;

    public Coupon? GetByCode(string code)
    {
        Log.Info("SyncCouponRepository -> Opening connection...");
        Thread.Sleep(200);

        Log.Info($"SyncCouponRepository -> Executing query for Coupon '{code}'...");
        Thread.Sleep(Random.Shared.Next(300, 601));

        Coupon? coupon = _store.ReadAll<Coupon>(DataFiles.Coupons)
            .FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

        if (coupon is null)
            Log.Info($"SyncCouponRepository -> Coupon '{code}' not found");
        else
            Log.Success($"SyncCouponRepository -> Coupon '{code}' found ({coupon.DiscountPercent}%)");

        return coupon;
    }
}