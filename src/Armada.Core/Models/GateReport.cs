namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// The acceptance gate report written to benchmarks/results (and uploaded to the Admiral): per-pipeline counts and the G1 to G12 measures. It
    /// names models and pipelines and never contains endpoint addresses or credentials.
    /// </summary>
    public sealed class GateReport
    {
        /// <summary>Id the Admiral assigned when the report was uploaded (POST /api/v1/reliability/gate-reports), or null.</summary>
        public string? Id { get; set; }

        /// <summary>When the Admiral stored the report, or null before upload.</summary>
        public DateTime? UploadedUtc { get; set; }

        /// <summary>When the gate started.</summary>
        public DateTime StartedUtc { get; set; }

        /// <summary>When the gate finished.</summary>
        public DateTime FinishedUtc { get; set; }

        /// <summary>Armada version reported by the Admiral.</summary>
        public string? ArmadaVersion { get; set; }

        /// <summary>Packs run.</summary>
        [CollectionBound(32, Reason = "One per battery pack the gate ran")]
        public List<string> Packs { get; set; } = new List<string>();

        /// <summary>Per-pipeline results.</summary>
        [CollectionBound(32, Reason = "One per reference pipeline the gate ran")]
        public List<GatePipelineResult> Pipelines { get; set; } = new List<GatePipelineResult>();

        /// <summary>The measures.</summary>
        [CollectionBound(64, Reason = "One per gate measure (G1 to G12 and their parts)")]
        public List<GateMeasure> Measures { get; set; } = new List<GateMeasure>();

        /// <summary>True when every measured measure passed and none failed.</summary>
        public bool Passed { get; set; }
    }
}
