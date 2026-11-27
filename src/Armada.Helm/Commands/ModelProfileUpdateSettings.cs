namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for the model profile update command. Each flag changes one field and leaves the rest as they are;
    /// a value of -1 clears that override so the global setting applies.
    /// </summary>
    public class ModelProfileUpdateSettings : ModelProfileIdSettings
    {
        /// <summary>Tier: Economy, Standard, Premium, or none to clear it.</summary>
        [Description("Tier: Economy, Standard, Premium, or none to clear it")]
        [CommandOption("--tier <TIER>")]
        public string? Tier { get; set; }

        /// <summary>Recovery: most resumes of a paused or failed attempt (0-10).</summary>
        [Description("Recovery: most resumes (0-10; -1 clears the override)")]
        [CommandOption("--max-resumes <N>")]
        public int? MaxResumes { get; set; }

        /// <summary>Recovery: most resumes after an unclassified failure (0-3).</summary>
        [Description("Recovery: most resumes after an Unclassified failure (0-3; -1 clears)")]
        [CommandOption("--unclassified-resumes <N>")]
        public int? UnclassifiedResumes { get; set; }

        /// <summary>Recovery: minutes a transient failure may retry before it counts (0-120).</summary>
        [Description("Recovery: transient grace in minutes (0-120; -1 clears)")]
        [CommandOption("--transient-grace <MINUTES>")]
        public int? TransientGraceMinutes { get; set; }

        /// <summary>Recovery: Judge rework rounds (1-5; can raise but never lower the global value).</summary>
        [Description("Recovery: Judge rework rounds (1-5, raises only; -1 clears)")]
        [CommandOption("--judge-rework-rounds <N>")]
        public int? JudgeReworkRounds { get; set; }

        /// <summary>Recovery: output repair attempts (0-3).</summary>
        [Description("Recovery: output repair attempts (0-3; -1 clears)")]
        [CommandOption("--output-repair-attempts <N>")]
        public int? OutputRepairAttempts { get; set; }

        /// <summary>Recovery: resumes after which a task failure goes to the persona escalation ladder (0-10).</summary>
        [Description("Recovery: escalate after this many resumes (0-10; -1 clears)")]
        [CommandOption("--escalate-after-resumes <N>")]
        public int? EscalateAfterResumes { get; set; }

        /// <summary>Timeouts: stall threshold in minutes.</summary>
        [Description("Timeouts: stall threshold in minutes (-1 clears)")]
        [CommandOption("--stall-minutes <MINUTES>")]
        public int? StallThresholdMinutes { get; set; }

        /// <summary>Timeouts: hard limit on one tool wait in minutes.</summary>
        [Description("Timeouts: tool wait hard limit in minutes (-1 clears)")]
        [CommandOption("--tool-wait-minutes <MINUTES>")]
        public int? ToolWaitHardLimitMinutes { get; set; }

        /// <summary>Timeouts: maximum mission runtime in minutes.</summary>
        [Description("Timeouts: maximum runtime in minutes (-1 clears)")]
        [CommandOption("--max-runtime-minutes <MINUTES>")]
        public int? MaxRuntimeMinutes { get; set; }

        /// <summary>Timeouts: minutes to wait for the first output.</summary>
        [Description("Timeouts: first output wait in minutes (-1 clears)")]
        [CommandOption("--first-output-minutes <MINUTES>")]
        public int? FirstOutputMinutes { get; set; }
    }
}
