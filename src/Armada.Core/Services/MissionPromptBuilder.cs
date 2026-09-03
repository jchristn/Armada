namespace Armada.Core.Services
{
    using System.Text.RegularExpressions;
    using Armada.Core;
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
                "Mux" => "MUX.md",
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
                ["MissionPersona"] = PersonaCatalog.NormalizeName(mission.Persona ?? PersonaCatalog.Worker),
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
        /// e.g. Test Engineer -> persona.test_engineer
        /// </summary>
        public static string GetPersonaTemplateName(string? persona)
        {
            if (String.IsNullOrEmpty(persona)) return "persona.worker";
            string normalizedPersona = PersonaCatalog.NormalizeName(persona);
            if (String.IsNullOrEmpty(normalizedPersona)) normalizedPersona = persona.Trim();
            string normalized = Regex.Replace(normalizedPersona, "([a-z0-9])([A-Z])", "$1_$2");
            normalized = Regex.Replace(normalized, "[\\s\\-]+", "_");
            normalized = Regex.Replace(normalized, "_+", "_").ToLowerInvariant();
            return "persona." + normalized;
        }

        /// <summary>
        /// Resolve the persona prompt for the mission. When a <paramref name="personaOverride"/> from a
        /// project profile is supplied and enabled, it swaps the persona's prompt template and/or appends
        /// per-project additional instructions to the rendered prompt.
        /// </summary>
        public static async Task<string> ResolvePersonaPromptAsync(
            string? persona,
            Dictionary<string, string> templateParams,
            IPromptTemplateService? promptTemplates,
            PersonaOverride? personaOverride = null,
            CancellationToken token = default)
        {
            if (templateParams == null) throw new ArgumentNullException(nameof(templateParams));

            bool overrideActive = personaOverride != null && personaOverride.Enabled;

            string templateName = GetPersonaTemplateName(persona);
            if (overrideActive && !String.IsNullOrWhiteSpace(personaOverride!.PromptTemplateName))
                templateName = personaOverride.PromptTemplateName!.Trim();

            string result = GetPersonaPromptFallback(persona);
            if (promptTemplates != null)
            {
                string rendered = await promptTemplates.RenderAsync(templateName, templateParams, token).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(rendered))
                    result = rendered;
            }

            if (overrideActive && !String.IsNullOrWhiteSpace(personaOverride!.AdditionalInstructions))
                result = result + "\n\n" + personaOverride.AdditionalInstructions!.Trim();

            return result;
        }

        /// <summary>
        /// Build the direct runtime launch prompt from the same shared context used by mission instructions.
        /// </summary>
        public static Task<string> BuildLaunchPromptAsync(
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
                return Task.FromResult(prompt);

            string overflowMessage = "\n\n" + instructionsFileName + " contains the remaining context. Keep working from that file if this launch prompt was truncated.";
            int allowed = Math.Max(256, MaxLaunchPromptChars - overflowMessage.Length);
            return Task.FromResult(prompt.Substring(0, allowed).TrimEnd() + overflowMessage);
        }

        private static string BuildBootstrapRoleSummary(string? persona)
        {
            return PersonaCatalog.NormalizeName(persona) switch
            {
                PersonaCatalog.Architect => "You are an Armada architect agent. Respond only with real [ARMADA:MISSION] blocks. Do not emit [ARMADA:RESULT] or [ARMADA:VERDICT] lines.",
                PersonaCatalog.ProductManager => "You are an Armada product manager agent. Include `## Product Vision`, `## Use Cases`, `## Experience Requirements`, `## Validation`, and `## Future Readiness` sections before a standalone [ARMADA:RESULT] COMPLETE line.",
                PersonaCatalog.UsabilityEngineer => "You are an Armada usability engineer agent. Include `## Usability`, `## Consistency`, `## Edge Cases`, and `## Residual Risks` sections before a standalone [ARMADA:RESULT] COMPLETE line.",
                PersonaCatalog.Worker => "You are an Armada worker agent. End with a standalone [ARMADA:RESULT] COMPLETE line followed by a brief plain-text summary.",
                PersonaCatalog.TestEngineer => "You are an Armada test engineer agent. Include `## Coverage Added`, `## Negative Paths`, and `## Residual Risks` sections before a standalone [ARMADA:RESULT] COMPLETE line.",
                PersonaCatalog.Judge => JudgeContract.RoleSummary(),
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
            if (PersonaCatalog.Matches(persona, PersonaCatalog.Architect))
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
            return PersonaCatalog.NormalizeName(persona) switch
            {
                PersonaCatalog.Architect =>
                    "Respond only with real [ARMADA:MISSION] blocks. Do not emit [ARMADA:RESULT] or [ARMADA:VERDICT] lines.",
                PersonaCatalog.ProductManager =>
                    "Before your result line, include `## Product Vision`, `## Use Cases`, `## Experience Requirements`, `## Validation`, and `## Future Readiness` sections. End with a standalone line `[ARMADA:RESULT] COMPLETE` followed by a brief plain-text summary.",
                PersonaCatalog.UsabilityEngineer =>
                    "Before your result line, include `## Usability`, `## Consistency`, `## Edge Cases`, and `## Residual Risks` sections. End with a standalone line `[ARMADA:RESULT] COMPLETE` followed by a brief plain-text summary.",
                PersonaCatalog.Worker =>
                    "Stay within scope, make the requested changes, and end with a standalone line `[ARMADA:RESULT] COMPLETE` followed by a brief plain-text summary.",
                PersonaCatalog.TestEngineer =>
                    "Before your result line, include short `## Coverage Added`, `## Negative Paths`, and `## Residual Risks` sections. End with a standalone line `[ARMADA:RESULT] COMPLETE` followed by a brief plain-text summary.",
                PersonaCatalog.Judge => JudgeContract.OutputContract(),
                _ => String.Empty
            };
        }

        private static string GetPersonaPromptFallback(string? persona)
        {
            return PersonaCatalog.NormalizeName(persona) switch
            {
                PersonaCatalog.Architect => "You are an Armada architect agent. Analyze the codebase and decompose the objective into right-sized missions using [ARMADA:MISSION] markers. Do not emit [ARMADA:RESULT] or [ARMADA:VERDICT] lines.",
                PersonaCatalog.ProductManager => "You are an Armada product manager agent. Clarify the whole product picture, define user value and experience requirements, include `## Product Vision`, `## Use Cases`, `## Experience Requirements`, `## Validation`, and `## Future Readiness` sections, and end with a standalone [ARMADA:RESULT] COMPLETE line.",
                PersonaCatalog.UsabilityEngineer => "You are an Armada usability engineer agent. Improve the work through the lens of usability, consistency, and edge-case handling, include `## Usability`, `## Consistency`, `## Edge Cases`, and `## Residual Risks` sections, and end with a standalone [ARMADA:RESULT] COMPLETE line.",
                PersonaCatalog.Worker => "You are an Armada worker agent. Implement the requested code changes carefully, stay within scope, and end with a standalone [ARMADA:RESULT] COMPLETE line.",
                PersonaCatalog.TestEngineer => "You are an Armada test engineer agent. Write tests for the current mission scope, cover negative and edge paths for validation, timeout, cancellation, retry, cleanup, and error-handling changes when applicable, include `## Coverage Added`, `## Negative Paths`, and `## Residual Risks` sections, and end with a standalone [ARMADA:RESULT] COMPLETE line.",
                PersonaCatalog.Judge => JudgeContract.PersonaPromptFallback(),
                _ => "You are an Armada captain executing a mission. Follow these instructions carefully."
            };
        }
    }
}
