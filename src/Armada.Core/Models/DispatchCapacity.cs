namespace Armada.Core.Models
{
    /// <summary>What the one capacity model says about starting any mission right now (RS-4.8).</summary>
    public sealed class DispatchCapacity
    {
        /// <summary>The deployment-wide MaxConcurrentMissions is full.</summary>
        public bool GlobalLimitReached { get; set; }

        /// <summary>At least one captain has room under its own, its runtime's and its endpoint's caps.</summary>
        public bool AnyCaptainHasRoom { get; set; }
    }
}
