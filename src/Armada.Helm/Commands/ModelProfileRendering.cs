namespace Armada.Helm.Commands
{
    using System;
    using System.Globalization;
    using Spectre.Console;
    using Armada.Core.Models;

    /// <summary>
    /// Console rendering for a model profile: identity, served context, tier, recovery policy and timeouts.
    /// </summary>
    public static class ModelProfileRendering
    {
        /// <summary>
        /// Write the profile summary.
        /// </summary>
        /// <param name="profile">Profile to render.</param>
        /// <exception cref="ArgumentNullException">When profile is null.</exception>
        public static void Write(ModelProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            ModelProfileIdentity id = profile.Identity;
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(id.Model ?? "-")}[/] [dim]{Markup.Escape(profile.Id)}[/] runtime {Markup.Escape(id.Runtime)}, provider {Markup.Escape(id.Provider ?? "-")}");
            string context = id.ServedContextTokens.HasValue
                ? id.ServedContextTokens.Value.ToString("N0", CultureInfo.InvariantCulture) + " tokens (" + (id.ServedContextSource?.ToString() ?? "unknown source") + ")"
                : "unknown";
            AnsiConsole.MarkupLine($"Served context {Markup.Escape(context)}");
            AnsiConsole.MarkupLine($"Tools {Flag(profile.Capabilities.ToolsAvailable, profile.Capabilities.ProbedToolsAvailable)}, MCP {Flag(profile.Capabilities.McpAvailable, profile.Capabilities.ProbedMcpAvailable)}");

            ModelProfileTiering tiering = profile.Tiering;
            string suggestion = tiering.SuggestedTier.HasValue
                ? ", suggested " + tiering.SuggestedTier.Value + (String.IsNullOrEmpty(tiering.SuggestedReason) ? "" : " (" + tiering.SuggestedReason + ")")
                : "";
            AnsiConsole.MarkupLine($"Tier {Markup.Escape(tiering.Tier?.ToString() ?? "none")}{Markup.Escape(suggestion)}");

            ModelProfileRecovery r = profile.Recovery;
            RecoveryPolicy? e = profile.EffectiveRecovery;
            AnsiConsole.MarkupLine("[dodgerblue1]Recovery[/] (override / in effect):");
            Row("Max resumes", r.MaxResumes, e?.MaxResumes);
            Row("Unclassified resumes", r.UnclassifiedResumes, null);
            Row("Transient grace minutes", r.TransientGraceMinutes, e?.TransientGraceMinutes);
            Row("Judge rework rounds", r.JudgeReworkRounds, e?.JudgeReworkRounds);
            Row("Output repair attempts", r.OutputRepairAttempts, e?.OutputRepairAttempts);
            Row("Escalate after resumes", r.EscalateAfterResumes, e?.EscalateAfterResumes);

            ModelProfileTimeouts t = profile.Timeouts;
            TimeoutPolicy? te = profile.EffectiveTimeouts;
            AnsiConsole.MarkupLine("[dodgerblue1]Timeouts in minutes[/] (override / in effect, source):");
            Row("Stall threshold", t.StallThresholdMinutes, te?.StallThresholdMinutes, te?.StallThresholdSource.ToString());
            Row("Tool wait hard limit", t.ToolWaitHardLimitMinutes, te?.ToolWaitHardLimitMinutes, te?.ToolWaitHardLimitSource.ToString());
            Row("Max runtime", t.MaxRuntimeMinutes, te?.MaxRuntimeMinutes, te?.MaxRuntimeSource.ToString());
            Row("First output", t.FirstOutputMinutes, te?.FirstOutputMinutes, te?.FirstOutputSource.ToString());
        }

        private static string Flag(bool? configured, bool? probed)
        {
            if (configured.HasValue) return (configured.Value ? "yes" : "no") + " (admin)";
            if (probed.HasValue) return (probed.Value ? "yes" : "no") + " (probed)";
            return "unknown";
        }

        private static void Row(string name, int? overrideValue, int? effective, string? source = null)
        {
            string o = overrideValue.HasValue ? overrideValue.Value.ToString(CultureInfo.InvariantCulture) : "-";
            string v = effective.HasValue ? effective.Value.ToString(CultureInfo.InvariantCulture) : "-";
            string s = String.IsNullOrEmpty(source) ? "" : ", " + source;
            AnsiConsole.MarkupLine("  " + Markup.Escape(name + ": " + o + " / " + v + s));
        }
    }
}
