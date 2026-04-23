namespace Armada.Core.Services
{
    using System.Text.RegularExpressions;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Shared mission prompt/context builder used by both CLAUDE.md generation
    /// and direct runtime launch prompts.
    /// </summary>
    public static class MissionPromptBuilder
    {
        private const int MaxLaunchPromptChars = 6000;
        private const int MaxPersonaSummaryChars = 320;
        private const int MaxCaptainInstructionChars = 800;
        private const int MaxMissionDescriptionChars = 3500;

        /// <summary>
        /// Resolve the runtime-specific mission instructions filename.
        /// </summary>
        public static string GetInstructionsFileName(string? runtime)
        {
            if (String.IsNullOrWhiteSpace(runtime)) return "CLAUDE.md";

            return runtime.Trim() switch
            {
                "ClaudeCode" => "CLAUDE.md",
                "Codex" => "CODEX.md",
                "Cursor" => "CURSOR.md",
                "Gemini" => "GEMINI.md",
                _ => "CLAUDE.md"
            };
        }

        /// <summary>
        /// Build a consistent template parameter dictionary for mission prompt rendering.
        /// </summary>
        public static Dictionary<string, string> BuildTemplateParams(
            Mission mission,
            Vessel vessel,
            Captain? captain = null,
            Dock? dock = null)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));

            return new Dictionary<string, string>
            {
                ["MissionId"] = mission.Id,
                ["MissionTitle"] = mission.Title,
                ["MissionDescription"] = mission.Description ?? "No additional description provided.",
                ["MissionPersona"] = mission.Persona ?? "Worker",
                ["VoyageId"] = mission.VoyageId ?? "",
                ["VesselId"] = vessel.Id,
                ["VesselName"] = vessel.Name,
                ["DefaultBranch"] = vessel.DefaultBranch,
                ["BranchName"] = dock?.BranchName ?? mission.BranchName ?? "unknown",
                ["FleetId"] = vessel.FleetId ?? "",
                ["ProjectContext"] = vessel.ProjectContext ?? "",
                ["StyleGuide"] = vessel.StyleGuide ?? "",
                ["ModelContext"] = vessel.EnableModelContext ? vessel.ModelContext ?? "" : "",
                ["SelectedPlaybooksMarkdown"] = "",
                ["CaptainId"] = captain?.Id ?? "",
                ["CaptainName"] = captain?.Name ?? "",
                ["CaptainInstructions"] = BuildCaptainInstructions(captain?.SystemInstructions, mission.Persona),
                ["Timestamp"] = DateTime.UtcNow.ToString("o")
            };
        }

        /// <summary>
        /// Normalize a persona name into the template naming convention.
        /// e.g. TestEngineer -> persona.test_engineer
        /// </summary>
        public static string GetPersonaTemplateName(string? persona)
        {
            if (String.IsNullOrEmpty(persona)) return "persona.worker";
            string normalized = Regex.Replace(persona, "(?<!^)([A-Z])", "_$1").ToLowerInvariant();
            return "persona." + normalized;
        }

        /// <summary>
        /// Resolve the persona prompt for the mission.
        /// </summary>
        public static async Task<string> ResolvePersonaPromptAsync(
            string? persona,
            Dictionary<string, string> templateParams,
            IPromptTemplateService? promptTemplates,
            CancellationToken token = default)
        {
            if (templateParams == null) throw new ArgumentNullException(nameof(templateParams));

            string templateName = GetPersonaTemplateName(persona);

            if (promptTemplates != null)
            {
                string rendered = await promptTemplates.RenderAsync(templateName, templateParams, token).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(rendered))
                    return rendered;
            }

            return GetPersonaPromptFallback(persona);
        }

        /// <summary>
        /// Build the direct runtime launch prompt from the same shared context used by mission instructions.
        /// </summary>
        public static async Task<string> BuildLaunchPromptAsync(
            Mission mission,
            Vessel vessel,
            Captain captain,
            Dock dock,
            IPromptTemplateService? promptTemplates,
            CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (dock == null) throw new ArgumentNullException(nameof(dock));

            string instructionsFileName = GetInstructionsFileName(captain.Runtime.ToString());

            List<string> sections = new List<string>();
            sections.Add("Role: " + BuildBootstrapRoleSummary(mission.Persona));
            sections.Add("Mission: " + mission.Title);
            sections.Add("Branch: " + (dock.BranchName ?? mission.BranchName ?? vessel.DefaultBranch ?? "main"));

            if (String.Equals(mission.Persona, "Architect", StringComparison.OrdinalIgnoreCase))
            {
                sections.Add(
                    "Read " + instructionsFileName + " in the working directory. " +
                    "It contains the objective, repository context, and mission-format requirements. " +
                    "Do not ask for more input. Read the file immediately and respond only with real [ARMADA:MISSION] blocks derived from that file.");
            }
            else
            {
                sections.Add(
                    "Read " + instructionsFileName + " in the working directory. " +
                    "It contains the full mission objective, repository context, style guide, model context, and execution rules. Do not ask for more input. Read the file immediately and follow it exactly.");
            }

            string prompt = String.Join(" ", sections.Select(s => s.Replace("\r", " ").Replace("\n", " ").Trim())).Trim();
            if (prompt.Length <= MaxLaunchPromptChars)
                return prompt;

            string overflowMessage = "\n\n" + instructionsFileName + " contains the remaining context. Keep working from that file if this launch prompt was truncated.";
            int allowed = Math.Max(256, MaxLaunchPromptChars - overflowMessage.Length);
            return prompt.Substring(0, allowed).TrimEnd() + overflowMessage;
        }

        private static string BuildBootstrapRoleSummary(string? persona)
        {
            return persona switch
            {
                "Architect" => "You are an Armada architect agent. Respond only with real [ARMADA:MISSION] blocks. Do not emit [ARMADA:RESULT] or [ARMADA:VERDICT] lines.",
                "Worker" => "You are an Armada worker agent. End with a standalone [ARMADA:RESULT] COMPLETE line followed by a brief plain-text summary.",
                "TestEngineer" => "You are an Armada test engineer agent. Include `## Coverage Added`, `## Negative Paths`, and `## Residual Risks` sections before a standalone [ARMADA:RESULT] COMPLETE line.",
                "Judge" => "You are an Armada judge agent. Include `## Completeness`, `## Correctness`, `## Tests`, `## Failure Modes`, and `## Verdict` sections, and end with exactly one standalone [ARMADA:VERDICT] PASS, [ARMADA:VERDICT] FAIL, or [ARMADA:VERDICT] NEEDS_REVISION line.",
                _ => "You are an Armada captain executing a mission."
            };
        }

        private static string SummarizeText(string? input, int maxChars)
        {
            if (String.IsNullOrWhiteSpace(input)) return "";

            string compact = Regex.Replace(input, "\\s+", " ").Trim();
            if (compact.Length <= maxChars) return compact;
            if (maxChars <= 3) return compact.Substring(0, maxChars);
            return compact.Substring(0, maxChars - 3).TrimEnd() + "...";
        }

        private static string BuildRoleSummary(string? persona, string personaSummary)
        {
            if (String.Equals(persona, "Architect", StringComparison.OrdinalIgnoreCase))
            {
                return "You are an Armada architect agent. Analyze the objective and decompose it into right-sized missions using [ARMADA:MISSION] markers. Do not emit [ARMADA:RESULT] or [ARMADA:VERDICT] lines.";
            }

            if (!String.IsNullOrEmpty(personaSummary))
                return personaSummary;

            return GetPersonaPromptFallback(persona);
        }

        private static string BuildCaptainInstructions(string? existingInstructions, string? persona)
        {
            string existing = existingInstructions?.Trim() ?? String.Empty;
            string outputContract = GetPersonaOutputContract(persona);

            if (String.IsNullOrEmpty(outputContract))
                return existing;

            if (String.IsNullOrEmpty(existing))
                return outputContract;

            return existing + "\n\n## Required Output Contract\n" + outputContract;
        }

        private static string GetPersonaOutputContract(string? persona)
        {
            return persona switch
            {
                "Architect" =>
                    "Respond only with real [ARMADA:MISSION] blocks. Do not emit [ARMADA:RESULT] or [ARMADA:VERDICT] lines.",
                "Worker" =>
                    "Stay within scope, make the requested changes, and end with a standalone line `[ARMADA:RESULT] COMPLETE` followed by a brief plain-text summary.",
                "TestEngineer" =>
                    "Before your result line, include short `## Coverage Added`, `## Negative Paths`, and `## Residual Risks` sections. End with a standalone line `[ARMADA:RESULT] COMPLETE` followed by a brief plain-text summary.",
                "Judge" =>
                    "Your response must contain these exact section headings: `## Completeness`, `## Correctness`, `## Tests`, `## Failure Modes`, and `## Verdict`. Do not reply with only a verdict line or brief summary. End with exactly one standalone line `[ARMADA:VERDICT] PASS`, `[ARMADA:VERDICT] FAIL`, or `[ARMADA:VERDICT] NEEDS_REVISION`.",
                _ => String.Empty
            };
        }

        private static string GetPersonaPromptFallback(string? persona)
        {
            return persona switch
            {
                "Architect" => "You are an Armada architect agent. Analyze the codebase and decompose the objective into right-sized missions using [ARMADA:MISSION] markers. Do not emit [ARMADA:RESULT] or [ARMADA:VERDICT] lines.",
                "Worker" => "You are an Armada worker agent. Implement the requested code changes carefully, stay within scope, and end with a standalone [ARMADA:RESULT] COMPLETE line.",
                "TestEngineer" => "You are an Armada test engineer agent. Write tests for the current mission scope, cover negative and edge paths for validation, timeout, cancellation, retry, cleanup, and error-handling changes when applicable, include `## Coverage Added`, `## Negative Paths`, and `## Residual Risks` sections, and end with a standalone [ARMADA:RESULT] COMPLETE line.",
                "Judge" => "You are an Armada judge agent. Review the completed work for completeness, correctness, test adequacy, and failure modes. Assume there may be a hidden bug. Use `## Completeness`, `## Correctness`, `## Tests`, `## Failure Modes`, and `## Verdict` sections, and end with exactly one standalone [ARMADA:VERDICT] PASS, [ARMADA:VERDICT] FAIL, or [ARMADA:VERDICT] NEEDS_REVISION line.",
                _ => "You are an Armada captain executing a mission. Follow these instructions carefully."
            };
        }
    }
}
