namespace Armada.Core.Services
{
    using System;

    /// <summary>Shared hard bounds for the v1.3 concurrency settings and future admission gates.</summary>
    public static class ConcurrencyClamp
    {
        /// <summary>Clamp a finite setting to its inclusive range.</summary>
        public static int Finite(int value, int minimum, int maximum) => Math.Clamp(value, minimum, maximum);

        /// <summary>Zero is unlimited; negative input must never accidentally enable unlimited work.</summary>
        public static int VesselLimit(int value) => value < 0 ? 1 : Math.Min(value, Constants.VesselConcurrencyCeiling);

        /// <summary>Captain slots are always finite and respect the configured server ceiling.</summary>
        public static int CaptainLimit(int value, int maximum = Constants.CaptainConcurrencyCeiling)
            => Math.Clamp(value, 1, Math.Clamp(maximum, 1, Constants.CaptainConcurrencyCeiling));

        /// <summary>Global mission limit: zero is unlimited, negative input becomes one.</summary>
        public static int GlobalMissionLimit(int value) => value < 0 ? 1 : Math.Min(value, Constants.GlobalMissionConcurrencyCeiling);

        /// <summary>Endpoint mission limits are finite, matching the existing 1..64 contract.</summary>
        public static int EndpointMissionLimit(int value) => Math.Clamp(value, 1, 64);
    }
}
