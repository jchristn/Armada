namespace Armada.Core.Services
{
    using System.Text;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    internal sealed class CaptainEligibilityContext
    {
        public string RoutingPersona { get; set; } = "";
        public HashSet<string> ExcludedAuthors { get; set; } = new HashSet<string>(StringComparer.Ordinal);
        public bool UsageFallbackAuthorized { get; set; }
        public bool UsageFallbackRequired { get; set; }
        public bool EscalatedToRequestedCaptain { get; set; } = false;
        public bool StrictCaptainOverride { get; set; } = false;
        public bool InactiveRequestedCaptain { get; set; } = false;
        public string? DefaultCaptainId { get; set; } = null;
        public Persona? Persona { get; set; } = null;
        public WorkerEnvelopeGate? EnvelopeGate { get; set; } = null;
        public Dictionary<string, string> ContextExclusions { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public JudgeStrengthPolicyEnum JudgePolicy { get; set; } = JudgeStrengthPolicyEnum.Off;
        public CaptainTierEnum? JudgeRequiredTier { get; set; } = null;
        public HashSet<string> SameFamilyAsAuthors { get; } = new HashSet<string>(StringComparer.Ordinal);
        public Dictionary<string, PersonaOutcomeScore> OutcomeScores { get; } = new Dictionary<string, PersonaOutcomeScore>(StringComparer.Ordinal);
    }
}
