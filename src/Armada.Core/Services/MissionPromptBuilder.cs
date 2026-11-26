namespace Armada.Core.Services
{
    using System.Text.RegularExpressions;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Shared mission prompt/context builder used by both CLAUDE.md generation
    /// and direct runtime launch prompts.
    /// </summary>
    public static class MissionPromptBuilder
    {
        private const int MaxLaunchPromptChars = 6000;

        /// <summary>
        /// Text placed in <c>{Diff}</c> when the mission has no prior pipeline stage diff.
        /// </summary>
        public const string NoPreviousStageDiffText = "No diff from a prior pipeline stage is available. Inspect the changes on the mission branch with git.";

        /// <summary>
        /// Text placed in <c>{PreviousStageOutput}</c> when the mission has no prior pipeline stage output.
        /// </summary>
        public const string NoPreviousStageOutputText = "No prior pipeline stage output is available.";

        /// <summary>
        /// Every placeholder (without braces) that mission prompt rendering supplies: the keys of
        /// <see cref="BuildTemplateParams(Mission, Vessel, Captain?, Dock?)"/> plus <c>PersonaPrompt</c> and
        /// <c>ExistingClaudeMd</c>, which mission instruction generation adds while it renders. The dashboard and TUI
        /// placeholder panels list exactly these.
        /// </summary>
        public static IReadOnlyList<string> TemplateParameterNames { get; } = new List<string>
        {
            "MissionId",
            "MissionTitle",
            "MissionDescription",
            "MissionCommitInstruction",
            "MissionPushInstruction",
            "MissionPersona",
            "VoyageId",
            "BranchName",
            "VesselId",
            "VesselName",
            "DefaultBranch",
            "ProjectContext",
            "StyleGuide",
            "ModelContext",
            "FleetId",
            "CaptainId",
            "CaptainName",
            "CaptainInstructions",
            "PersonaPrompt",
            "Diff",
            "PreviousStageOutput",
            "SelectedPlaybooksMarkdown",
            "WorkerEnvelope",
            "MaxParallelMissionsPerVoyage",
            "OwnedPaths",
            "ConcurrentSiblingPaths",
            "ParentSummary",
            "ExistingClaudeMd",
            "Timestamp"
        };

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
                // pi loads one context file per directory: AGENTS.override.md, else AGENTS.md, else CLAUDE.md. Only the
                // override is always read, so a repository AGENTS.md cannot hide the mission instructions.
                "Pi" => "AGENTS.override.md",
                _ => "CLAUDE.md"
            };
        }

        /// <summary>
        /// Build a consistent template parameter dictionary for mission prompt rendering without a prior stage:
        /// <c>{Diff}</c> and <c>{PreviousStageOutput}</c> hold <see cref="NoPreviousStageDiffText"/> and
        /// <see cref="NoPreviousStageOutputText"/>. A launch passes the stored handoff it rendered instead.
        /// </summary>
        public static Dictionary<string, string> BuildTemplateParams(
            Mission mission,
            Vessel vessel,
            Captain? captain = null,
            Dock? dock = null)
        {
            return BuildTemplateParams(mission, vessel, captain, dock, null, null);
        }

        /// <summary>
        /// Build the template parameter dictionary with the prior stage's handoff already rendered for the reader:
        /// <c>{PreviousStageOutput}</c> holds the evidence block (findings and required actions first, checks, output,
        /// the pointer to the complete artifact and what did not fit) and <c>{Diff}</c> the relevant diff sections.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <param name="vessel">Vessel.</param>
        /// <param name="captain">Captain, or null.</param>
        /// <param name="dock">Dock, or null.</param>
        /// <param name="previousStage">The prior pipeline stage mission, or null.</param>
        /// <param name="handoff">The prior stage's rendered handoff, or null when there is no prior stage.</param>
        /// <returns>Placeholder values keyed by name (without braces).</returns>
        public static Dictionary<string, string> BuildTemplateParams(
            Mission mission,
            Vessel vessel,
            Captain? captain,
            Dock? dock,
            Mission? previousStage,
            HandoffRendering? handoff)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));

            return new Dictionary<string, string>
            {
                ["MissionId"] = mission.Id,
                ["MissionTitle"] = mission.Title,
                ["MissionDescription"] = String.IsNullOrWhiteSpace(MissionNotes.EffectiveDescription(mission)) ? "No additional description provided." : MissionNotes.EffectiveDescription(mission),
                ["MissionCommitInstruction"] = "",
                ["MissionPushInstruction"] = "",
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
                ["MaxParallelMissionsPerVoyage"] = vessel.MaxParallelMissionsPerVoyage == 0 ? "none (captain capacity still applies)" : vessel.MaxParallelMissionsPerVoyage.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["OwnedPaths"] = DescribeOwnedPaths(mission.OwnedPathsJson),
                ["ConcurrentSiblingPaths"] = MergeConflictPrompt.NoConcurrentPaths,
                ["ParentSummary"] = "",
                ["WorkerEnvelope"] = WorkerEnvelopeRules.PromptSection(WorkerEnvelopeRules.Effective(vessel.WorkerEnvelope, mission.WorkerEnvelope, previousStage?.WorkerEnvelope), mission.Persona),
                ["CaptainId"] = captain?.Id ?? "",
                ["CaptainName"] = captain?.Name ?? "",
                ["CaptainInstructions"] = BuildCaptainInstructions(captain?.SystemInstructions, mission.Persona),
                ["Diff"] = previousStage == null || handoff == null ? NoPreviousStageDiffText : handoff.Diff,
                ["PreviousStageOutput"] = previousStage == null || handoff == null ? NoPreviousStageOutputText : handoff.Evidence,
                ["Timestamp"] = DateTime.UtcNow.ToString("o")
            };
        }

        /// <summary>
        /// Render a mission's stored owned paths (a JSON string array) for prompts, or the description fallback.
        /// </summary>
        /// <param name="ownedPathsJson">Stored <c>OwnedPathsJson</c>.</param>
        /// <returns>Comma-separated paths in backticks, or <see cref="MergeConflictPrompt.NoOwnedPaths"/>.</returns>
        public static string DescribeOwnedPaths(string? ownedPathsJson)
        {
            List<string> paths = OwnedPathList(ownedPathsJson);
            return paths.Count == 0 ? MergeConflictPrompt.NoOwnedPaths : String.Join(", ", paths.Select(p => "`" + p + "`"));
        }

        /// <summary>Parse stored owned or read paths; malformed or empty JSON yields an empty list.</summary>
        /// <param name="pathsJson">A JSON string array.</param>
        /// <returns>The paths.</returns>
        public static List<string> OwnedPathList(string? pathsJson) => MissionOwnership.Paths(pathsJson);

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
        public static Task<string> ResolvePersonaPromptAsync(
            string? persona,
            Dictionary<string, string> templateParams,
            IPromptTemplateService? promptTemplates,
            PersonaOverride? personaOverride = null,
            CancellationToken token = default)
        {
            return ResolvePersonaPromptAsync(persona, null, templateParams, promptTemplates, personaOverride, token);
        }

        /// <summary>
        /// The template name of a persona prompt variant: the conventional <c>persona.&lt;name&gt;</c> template for
        /// <see cref="PromptVariantEnum.Current"/>, else that name with a <c>.lean</c> or <c>.compact</c> suffix. A
        /// name that already starts with <c>persona.</c> is taken as the persona template name.
        /// </summary>
        /// <param name="persona">Persona name (for example "Test Engineer") or persona template name.</param>
        /// <param name="variant">Variant.</param>
        /// <returns>Template name.</returns>
        public static string GetPersonaVariantTemplateName(string? persona, PromptVariantEnum variant)
        {
            string baseName = !String.IsNullOrWhiteSpace(persona) && persona.Trim().StartsWith("persona.", StringComparison.Ordinal)
                ? persona.Trim()
                : GetPersonaTemplateName(persona);
            return variant switch
            {
                PromptVariantEnum.Lean => baseName + ".lean",
                PromptVariantEnum.Compact => baseName + ".compact",
                _ => baseName
            };
        }

        /// <summary>
        /// Resolve the persona prompt for the mission using the persona's own prompt template
        /// (<see cref="Persona.PromptTemplateName"/>) with the <see cref="PromptVariantEnum.Current"/> variant. See
        /// <see cref="ResolvePersonaPromptAsync(string?, string?, PromptVariantEnum, Dictionary{string, string}, IPromptTemplateService?, PersonaOverride?, CancellationToken)"/>.
        /// </summary>
        /// <param name="persona">Mission persona name.</param>
        /// <param name="personaTemplateName">The persona's configured prompt template name, or null for the convention.</param>
        /// <param name="templateParams">Template parameters.</param>
        /// <param name="promptTemplates">Template service, or null.</param>
        /// <param name="personaOverride">Project-profile persona override, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The resolved persona prompt.</returns>
        public static Task<string> ResolvePersonaPromptAsync(
            string? persona,
            string? personaTemplateName,
            Dictionary<string, string> templateParams,
            IPromptTemplateService? promptTemplates,
            PersonaOverride? personaOverride = null,
            CancellationToken token = default)
        {
            return ResolvePersonaPromptAsync(persona, personaTemplateName, PromptVariantEnum.Current, templateParams, promptTemplates, personaOverride, token);
        }

        /// <summary>
        /// Resolve the persona prompt for the mission using the persona's own prompt template
        /// (<see cref="Persona.PromptTemplateName"/>) when one is supplied. Precedence: an enabled project-profile
        /// override's template, then <paramref name="personaTemplateName"/> when it names a template other than the
        /// conventional one, then the <paramref name="variant"/> of the conventional template
        /// (<see cref="GetPersonaVariantTemplateName"/>; Current is the conventional <c>persona.&lt;name&gt;</c>
        /// template itself); a template that renders empty (missing) falls back to the conventional template and then
        /// to the built-in role summary.
        /// </summary>
        /// <param name="persona">Mission persona name.</param>
        /// <param name="personaTemplateName">The persona's configured prompt template name, or null for the convention.</param>
        /// <param name="variant">Prompt variant (<see cref="PromptVariantResolver"/>); Current renders exactly as before.</param>
        /// <param name="templateParams">Template parameters.</param>
        /// <param name="promptTemplates">Template service, or null.</param>
        /// <param name="personaOverride">Project-profile persona override, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The resolved persona prompt.</returns>
        public static async Task<string> ResolvePersonaPromptAsync(
            string? persona,
            string? personaTemplateName,
            PromptVariantEnum variant,
            Dictionary<string, string> templateParams,
            IPromptTemplateService? promptTemplates,
            PersonaOverride? personaOverride = null,
            CancellationToken token = default)
        {
            if (templateParams == null) throw new ArgumentNullException(nameof(templateParams));

            PromptTemplate? chosen = await ChoosePersonaTemplateAsync(persona, personaTemplateName, variant, promptTemplates, personaOverride, token).ConfigureAwait(false);
            string body = await RenderPersonaPromptBodyAsync(persona, chosen, templateParams, promptTemplates, personaOverride, token).ConfigureAwait(false);

            // The one output contract, generated from the models the parsers read (StageOutputContract).
            return StageOutputContract.AppendTo(persona, body);
        }

        /// <summary>
        /// The persona prompt template a launch renders, chosen with the precedence of
        /// <see cref="ResolvePersonaPromptAsync(string?, string?, PromptVariantEnum, Dictionary{string, string}, IPromptTemplateService?, PersonaOverride?, CancellationToken)"/>:
        /// an enabled project-profile override's template, then the persona's configured template, then the variant of the
        /// conventional template; a missing or empty template falls back to the conventional one. The instructions composer
        /// reads the chosen template's declared slots (for example <c>{PreviousStageOutput}</c>) before rendering it once.
        /// </summary>
        /// <param name="persona">Mission persona name.</param>
        /// <param name="personaTemplateName">The persona's configured prompt template name, or null for the convention.</param>
        /// <param name="variant">Prompt variant.</param>
        /// <param name="promptTemplates">Template service, or null.</param>
        /// <param name="personaOverride">Project-profile persona override, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The chosen template, or null when none resolves (the built-in role summary is used).</returns>
        public static async Task<PromptTemplate?> ChoosePersonaTemplateAsync(
            string? persona,
            string? personaTemplateName,
            PromptVariantEnum variant,
            IPromptTemplateService? promptTemplates,
            PersonaOverride? personaOverride = null,
            CancellationToken token = default)
        {
            if (promptTemplates == null) return null;

            string conventionalName = GetPersonaTemplateName(persona);
            string templateName = String.IsNullOrWhiteSpace(personaTemplateName) ? conventionalName : personaTemplateName!.Trim();
            if (variant != PromptVariantEnum.Current && String.Equals(templateName, conventionalName, StringComparison.Ordinal))
                templateName = GetPersonaVariantTemplateName(persona, variant);
            if (personaOverride != null && personaOverride.Enabled && !String.IsNullOrWhiteSpace(personaOverride.PromptTemplateName))
                templateName = personaOverride.PromptTemplateName!.Trim();

            PromptTemplate? chosen = await promptTemplates.ResolveAsync(templateName, token).ConfigureAwait(false);
            if ((chosen == null || String.IsNullOrEmpty(chosen.Content)) && !String.Equals(templateName, conventionalName, StringComparison.Ordinal))
                chosen = await promptTemplates.ResolveAsync(conventionalName, token).ConfigureAwait(false);
            return chosen == null || String.IsNullOrEmpty(chosen.Content) ? null : chosen;
        }

        /// <summary>
        /// Render a chosen persona template (or the built-in role summary when none was chosen) with the override's
        /// additional instructions, without the output contract. The instructions composer places the contract itself, at
        /// the end of the file.
        /// </summary>
        /// <param name="persona">Mission persona name.</param>
        /// <param name="chosen">Template from <see cref="ChoosePersonaTemplateAsync"/>, or null.</param>
        /// <param name="templateParams">Template parameters.</param>
        /// <param name="promptTemplates">Template service, or null.</param>
        /// <param name="personaOverride">Project-profile persona override, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The persona prompt without its contract.</returns>
        /// <exception cref="ArgumentNullException">Thrown when templateParams is null.</exception>
        public static async Task<string> RenderPersonaPromptBodyAsync(
            string? persona,
            PromptTemplate? chosen,
            Dictionary<string, string> templateParams,
            IPromptTemplateService? promptTemplates,
            PersonaOverride? personaOverride = null,
            CancellationToken token = default)
        {
            if (templateParams == null) throw new ArgumentNullException(nameof(templateParams));

            string result = GetPersonaPromptFallback(persona);
            if (promptTemplates != null && chosen != null)
            {
                string rendered = await promptTemplates.RenderAsync(chosen.Name, templateParams, token).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(rendered)) result = rendered;
            }

            if (personaOverride != null && personaOverride.Enabled && !String.IsNullOrWhiteSpace(personaOverride.AdditionalInstructions))
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

            if (PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Architect))
            {
                sections.Add(
                    "Read " + instructionsFileName + " in the working directory. " +
                    "It contains the objective, repository context, and mission-format requirements. " +
                    "Do not ask for more input. Read the file immediately and respond only with the plan its Output Contract section describes.");
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
            return StageOutputContract.RoleSummary(persona);
        }

        private static string BuildCaptainInstructions(string? existingInstructions, string? persona)
        {
            // The output contract is appended to the persona prompt once (StageOutputContract), not repeated here.
            return existingInstructions?.Trim() ?? String.Empty;
        }

        private static string GetPersonaPromptFallback(string? persona)
        {
            return PersonaCatalog.NormalizeName(persona) switch
            {
                PersonaCatalog.Architect => "You are an Armada architect agent. Analyze the codebase and decompose the objective into bounded, independently verifiable slices. Use parent only where a slice needs another's code, and give slices that can run at the same time disjoint ownedPaths.",
                PersonaCatalog.ProductManager => "You are an Armada product manager agent. Clarify the whole product picture and define user value and experience requirements.",
                PersonaCatalog.UsabilityEngineer => "You are an Armada usability engineer agent. Improve the work through the lens of usability, consistency, and edge-case handling.",
                PersonaCatalog.Worker => "You are an Armada worker agent. Implement the requested code changes carefully and stay within scope.",
                PersonaCatalog.TestEngineer => "You are an Armada test engineer agent. Write tests for the current mission scope, covering negative and edge paths for validation, timeout, cancellation, retry, cleanup, and error-handling changes when applicable.",
                PersonaCatalog.Linter => "You are an Armada linter agent. Evaluate the changed code and documentation for style and correctness, fix clear in-scope violations and flag judgment calls.",
                PersonaCatalog.Judge => "You are an Armada judge agent. Review the completed work through three lenses (correctness, blast radius, and source fidelity) and assume there may be a hidden defect.",
                _ => "You are an Armada captain executing a mission. Follow these instructions carefully."
            };
        }
    }
}
