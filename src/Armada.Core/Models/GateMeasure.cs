namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// One acceptance gate measure (G1 to G12 in RELIABILITY_SIMPLICITY_PLAN.md) with its value and threshold.
    /// </summary>
    public sealed class GateMeasure
    {
        /// <summary>Measure id, G1 to G12.</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>What is measured.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Measured value as text, or empty when not measured.</summary>
        public string Value { get; set; } = String.Empty;

        /// <summary>Threshold as text.</summary>
        public string Threshold { get; set; } = String.Empty;

        /// <summary>Result.</summary>
        public GateMeasureStatusEnum Status { get; set; } = GateMeasureStatusEnum.NotMeasured;

        /// <summary>Counts behind the value, or why it was not measured.</summary>
        public string Detail { get; set; } = String.Empty;
    }
}
