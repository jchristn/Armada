namespace Armada.Core.Models
{
    /// <summary>Stable values of a captain-selection decision's <c>Trigger</c>: what asked Armada to choose a captain.</summary>
    public static class CaptainSelectionTriggers
    {
        /// <summary>First dispatch of a mission.</summary>
        public const string Dispatch = "Dispatch";

        /// <summary>Armada dispatching a mission again by itself after an attempt that produced no work or was interrupted.</summary>
        public const string Redispatch = "Redispatch";

        /// <summary>Verification repair admission.</summary>
        public const string Repair = "Repair";

        /// <summary>A recorded wait; no captain was admitted.</summary>
        public const string Waiting = "Waiting";
    }
}
