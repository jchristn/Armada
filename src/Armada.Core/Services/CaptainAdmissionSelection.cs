namespace Armada.Core.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    internal sealed class CaptainAdmissionSelection
    {
        internal Mission Mission = null!;
        internal CaptainEligibilityContext Eligibility = null!;
        internal List<Captain> Visible = new();
        internal List<Captain> Available = new();
        internal List<Captain> RoutingEligible = new();
        internal Dictionary<string, MissionAssignmentBlocker> CapacityExclusions = new(StringComparer.Ordinal);
        internal Captain? Selected;
        internal List<CaptainRoutingBucket> RoutingBuckets = new List<CaptainRoutingBucket>();
    }
}
