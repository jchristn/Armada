namespace Armada.Core.Services
{
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>Operator-facing background work outcomes, without exposing opaque job results.</summary>
    public static class JobActivity
    {
        internal static readonly string[] BenignUsageFailures = { "SettingsGenerationChanged", "CaptureDisabled", "NoMembers" };

        /// <summary>Whether an unsuccessful task currently warrants operator attention.</summary>
        public static bool NeedsAttention(Job job) => job.Status == JobStatusEnum.Failed
            && !(job.Kind == JobKindEnum.UsageProbe && BenignUsageFailures.Contains(job.ErrorReason, StringComparer.Ordinal));

        private static string[]? UsageParts(Job job) => job.Kind == JobKindEnum.UsageProbe && job.Name.StartsWith("usage-capture:", StringComparison.Ordinal)
            ? UsageAccountResolver.SplitGroupKey(job.Name.Substring("usage-capture:".Length)) : null;

        /// <summary>A readable task name; account capture names retain runtime context.</summary>
        public static string DisplayName(Job job)
        {
            string[]? parts = UsageParts(job);
            return job.Kind == JobKindEnum.UsageProbe
                ? "Captain usage capture" + (parts == null ? "" : " (" + parts[2] + ")") : job.Name;
        }

        /// <summary>Open the task's existing workflow; generic tasks use the event log.</summary>
        public static string Href(Job job)
        {
            if (job.Kind == JobKindEnum.UsageProbe)
            {
                string[]? parts = UsageParts(job);
                return "/activity?source=usage-overview" + (parts == null || parts[1] == "-" ? "" : "&hostId=" + Uri.EscapeDataString(parts[1]));
            }
            if (job.Kind == JobKindEnum.Report && job.Name == "Vessel health evaluation") return "/vessels/health";
            if (job.Kind == JobKindEnum.VesselImport || job.Kind == JobKindEnum.VesselDiscovery || job.Kind == JobKindEnum.FleetCategorization)
            {
                Match batch = Regex.Match(job.Name, @"vib_[A-Za-z0-9_-]+");
                if (batch.Success) return "/vessels/import?batch=" + Uri.EscapeDataString(batch.Value);
            }
            return "/activity?source=events";
        }

        /// <summary>Build one scoped audit record for a winning terminal transition.</summary>
        public static ArmadaEvent Event(Job job)
        {
            string type = "job." + job.Status.ToString().ToLowerInvariant();
            string name = DisplayName(job);
            return new ArmadaEvent(type, name + ": " + job.Status + (String.IsNullOrWhiteSpace(job.ErrorReason) ? "" : ". " + job.ErrorReason))
            {
                TenantId = job.TenantId, UserId = job.UserId, EntityType = "job", EntityId = job.Id,
                Payload = JsonSerializer.Serialize(new { JobId = job.Id, Name = job.Name, Kind = job.Kind.ToString(), Status = job.Status.ToString(), job.Progress, job.ErrorReason, Href = Href(job) })
            };
        }
    }
}
