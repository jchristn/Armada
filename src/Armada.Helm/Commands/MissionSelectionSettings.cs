namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using Spectre.Console;
    using Spectre.Console.Cli;

    /// <summary>Scoped mission selection summary, history, immutable detail or read-only preview.</summary>
    public sealed class MissionSelectionSettings : BaseSettings
    {
        /// <summary>Mission identifier; name-based broad searches are not performed.</summary>
        [CommandArgument(0, "<mission>")]
        [Description("Mission identifier")]
        public string MissionId { get; set; } = String.Empty;
        /// <summary>Read a filtered page of retained decisions.</summary>
        [CommandOption("--history")]
        [Description("Show paged selection history")]
        public bool History { get; set; }
        /// <summary>Evaluate current state without capture, claim, launch or history writes.</summary>
        [CommandOption("--preview")]
        [Description("Preview current selection without changing the mission")]
        public bool Preview { get; set; }
        /// <summary>Read one retained decision belonging to the mission.</summary>
        [CommandOption("--decision <id>")]
        [Description("Inspect a retained selection decision and candidate page")]
        public string? DecisionId { get; set; }
        /// <summary>Explicit tenant for a global administrator reading standalone detail.</summary>
        [CommandOption("--tenant <id>")]
        [Description("Explicit tenant for --decision (global administrator only)")]
        public string? TenantId { get; set; }
        /// <summary>Optional exact history outcome.</summary>
        [CommandOption("--outcome <outcome>")]
        [Description("History outcome: Admitted or Waiting")]
        public string? Outcome { get; set; }

        /// <summary>Reject ambiguous modes and invalid paging before making requests.</summary>
        public override ValidationResult Validate()
        {
            if (String.IsNullOrWhiteSpace(MissionId)) return ValidationResult.Error("A mission identifier is required.");
            if ((History ? 1 : 0) + (Preview ? 1 : 0) + (DecisionId != null ? 1 : 0) > 1) return ValidationResult.Error("Choose only one of --history, --preview or --decision.");
            if (Outcome != null && (!History || Outcome != "Admitted" && Outcome != "Waiting")) return ValidationResult.Error("--outcome requires --history and must be Admitted or Waiting.");
            if (TenantId != null && DecisionId == null) return ValidationResult.Error("--tenant applies only to --decision.");
            if (Page < 1 || PageSize < 1 || PageSize > 1000) return ValidationResult.Error("--page must be positive and --page-size must be 1..1000.");
            return ValidationResult.Success();
        }
    }
}
