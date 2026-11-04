namespace Armada.Core.Models
{
    /// <summary>Stable persisted decision outcome codes. Values are stored and returned verbatim; never rename them.</summary>
    public static class CaptainSelectionOutcomes
    {
        /// <summary>An actual attempt was admitted and a captain slot was claimed.</summary>
        public const string Admitted = "Admitted";

        /// <summary>No attempt was admitted; the mission remains pending.</summary>
        public const string Waiting = "Waiting";

        /// <summary>Observe mode only: the hypothetical enforced evaluation would have selected a captain.</summary>
        public const string Selected = "Selected";
    }
}
