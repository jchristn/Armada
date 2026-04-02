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
                ["CaptainId"] = captain?.Id ?? "",
                ["CaptainName"] = captain?.Name ?? "",
                ["CaptainInstructions"] = captain?.SystemInstructions ?? "",
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

            Dictionary<string, string> templateParams = BuildTemplateParams(mission, vessel, captain, dock);
            string personaPrompt = await ResolvePersonaPromptAsync(mission.Persona, templateParams, promptTemplates, token).ConfigureAwait(false);

            string renderedLaunch = "";
            if (promptTemplates != null)
            {
                renderedLaunch = await promptTemplates.RenderAsync("agent.launch_prompt", templateParams, token).ConfigureAwait(false);
            }
            if (String.IsNullOrEmpty(renderedLaunch))
            {
                renderedLaunch = "Mission: " + mission.Title + "\n\n" + (mission.Description ?? "");
            }

            List<string> sections = new List<string>();
            if (!String.IsNullOrEmpty(personaPrompt))
                sections.Add(personaPrompt);
            if (!String.IsNullOrEmpty(captain.SystemInstructions))
                sections.Add("## Captain Instructions\n" + captain.SystemInstructions);
            if (!String.IsNullOrEmpty(vessel.ProjectContext))
                sections.Add("## Project Context\n" + vessel.ProjectContext);
            if (!String.IsNullOrEmpty(vessel.StyleGuide))
                sections.Add("## Style Guide\n" + vessel.StyleGuide);
            if (vessel.EnableModelContext && !String.IsNullOrEmpty(vessel.ModelContext))
                sections.Add("## Model Context\n" + vessel.ModelContext);
            sections.Add(renderedLaunch);

            return String.Join("\n\n", sections);
        }

        private static string GetPersonaPromptFallback(string? persona)
        {
            return persona switch
            {
                "Architect" => "You are an Armada architect agent. Analyze the codebase and decompose the objective into right-sized missions using [ARMADA:MISSION] markers.",
                "Worker" => "You are an Armada worker agent. Implement the requested code changes carefully, stay within scope, and finish with [ARMADA:RESULT] COMPLETE or [ARMADA:RESULT] FAIL.",
                "TestEngineer" => "You are an Armada test engineer agent. Write tests that verify the changes made in the previous stage and finish with [ARMADA:RESULT] COMPLETE or [ARMADA:RESULT] FAIL.",
                "Judge" => "You are an Armada judge agent. Review the completed work for correctness, completeness, and scope compliance, then end with [ARMADA:VERDICT] PASS, [ARMADA:VERDICT] FAIL, or [ARMADA:VERDICT] NEEDS_REVISION.",
                _ => "You are an Armada captain executing a mission. Follow these instructions carefully."
            };
        }
    }
}
