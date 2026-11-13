namespace Armada.Core.Services
{
    using System;

    // Match the ledger's DECIMAL(18,6) precision without understating a reservation or overstating headroom.
    internal static class UsageSelectionNumbers
    {
        internal static double Up(double value) => (double)(Decimal.Ceiling((decimal)value * 1000000m) / 1000000m);
        internal static double? Headroom(double? value) => value.HasValue && Double.IsFinite(value.Value) && value >= 0
            ? (double)(Decimal.Floor((decimal)value.Value * 1000000m) / 1000000m) : null;
    }
}
