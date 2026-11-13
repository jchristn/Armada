namespace Armada.Core.Models
{
    using System;

    /// <summary>The number of visible evaluated candidates with one primary reason that a decision did not store individually.</summary>
    public sealed class CaptainSelectionOmittedReason
    {
        /// <summary>Primary reason code shared by the omitted candidates.</summary>
        public string ReasonCode { get; set; } = String.Empty;
        /// <summary>Number of omitted candidates with this reason.</summary>
        public int Count { get; set; }
    }
}
