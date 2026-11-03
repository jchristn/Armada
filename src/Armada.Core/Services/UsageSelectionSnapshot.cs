namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>Internal evaluation snapshot; never serialize its account identities to client responses.</summary>
    internal sealed class UsageSelectionSnapshot
    {
        internal UsageSelectionSettings Settings { get; set; } = new UsageSelectionSettings();
        internal UsageSelectionEvaluation Evaluation { get; set; } = new UsageSelectionEvaluation();
        internal Dictionary<string, UsageSelectionAccountEvidence> Evidence { get; set; } = new Dictionary<string, UsageSelectionAccountEvidence>(StringComparer.Ordinal);
        internal Dictionary<string, Captain> Captains { get; set; } = new Dictionary<string, Captain>(StringComparer.Ordinal);
        internal string? ActualCaptainId { get; set; }
        internal bool CaptureEnabled { get; set; }
        internal DateTime EvaluatedUtc { get; set; }
    }
}
