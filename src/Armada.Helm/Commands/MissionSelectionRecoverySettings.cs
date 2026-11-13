namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using Spectre.Console;
    using Spectre.Console.Cli;

    /// <summary>Audited recovery permissions for one Pending mission's captain selection.</summary>
    public sealed class MissionSelectionRecoverySettings : OutputSettings
    {
        /// <summary>Mission identifier.</summary>
        [CommandArgument(0, "<mission>")]
        [Description("Mission ID (msn_)")]
        public string MissionId { get; set; } = String.Empty;

        /// <summary>Permit an eligible fallback from a non-strict preferred captain.</summary>
        [CommandOption("--allow-fallback")]
        [Description("Allow an eligible fallback when the preferred captain cannot run (strict assignments stay strict)")]
        public bool AllowFallback { get; set; }

        /// <summary>Permit use of the protected soft reserve; hard quota, cooldowns and capacity still apply.</summary>
        [CommandOption("--reserve-exception")]
        [Description("Allow the soft-reserve exception (exhausted quota and cooldowns still block)")]
        public bool ReserveException { get; set; }

        /// <summary>Required audited reason.</summary>
        [CommandOption("--reason <TEXT>")]
        [Description("Required reason recorded in the audit history (1-512 characters, no credentials)")]
        public string? Reason { get; set; }

        /// <summary>Optimistic revision; when omitted the current revision is read and shown first.</summary>
        [CommandOption("--expected-revision <N>")]
        [Description("Revision being replaced (default: the current revision, read first)")]
        public int? ExpectedRevision { get; set; }

        /// <summary>Skip the confirmation prompt.</summary>
        [CommandOption("-y|--yes")]
        [Description("Do not ask for confirmation")]
        public bool Yes { get; set; }

        /// <summary>Validate before making requests.</summary>
        /// <returns>Validation result.</returns>
        public override ValidationResult Validate()
        {
            if (String.IsNullOrWhiteSpace(MissionId)) return ValidationResult.Error("A mission identifier is required.");
            if (String.IsNullOrWhiteSpace(Reason)) return ValidationResult.Error("--reason is required and is recorded in the audit history.");
            if (Reason!.Trim().Length > 512) return ValidationResult.Error("--reason must be at most 512 characters.");
            if (ExpectedRevision < 0) return ValidationResult.Error("--expected-revision must be zero or greater.");
            return ValidationResult.Success();
        }
    }
}
