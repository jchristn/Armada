namespace Armada.Core.Services
{
    using System.Text;
    using System.Text.Json;
    using SyslogLogging;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Mission instructions: generating the instruction file, playbooks, persona templates, skills and prior-stage context.
    /// </summary>
    public partial class MissionService
    {
        #region Public-Methods

        /// <inheritdoc />
        public async Task RegenerateInstructionsAsync(Mission mission, Captain captain, Dock dock, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (dock == null) throw new ArgumentNullException(nameof(dock));
            if (String.IsNullOrEmpty(dock.WorktreePath) || String.IsNullOrEmpty(mission.VesselId)) return;
            Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId!, token).ConfigureAwait(false);
            if (vessel == null) return;
            DockHost? harborHost = HarborHostFor(dock);
            await GenerateClaudeMdOnHostAsync(harborHost, dock.WorktreePath!, mission, vessel, captain, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task GenerateClaudeMdAsync(string worktreePath, Mission mission, Vessel vessel, Captain? captain = null, CancellationToken token = default)
        {
            return GenerateClaudeMdOnHostAsync(null, worktreePath, mission, vessel, captain, token);
        }

        /// <summary>
        /// Generate the mission's instruction file into a dock: on this machine when <paramref name="harborHost"/> is null,
        /// else on that Harbor (file writes, git anchors, and exclude entries all go through the Harbor).
        /// </summary>
        private async Task GenerateClaudeMdOnHostAsync(DockHost? harborHost, string worktreePath, Mission mission, Vessel vessel, Captain? captain, CancellationToken token)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));

            string instructionsFileName = MissionPromptBuilder.GetInstructionsFileName(captain != null ? captain.Runtime.ToString() : null);
            string instructionsPath = Path.Combine(worktreePath, instructionsFileName);

            Mission? previousStage = await ReadPreviousStageAsync(mission, token).ConfigureAwait(false);
            HandoffRendering? handoff = previousStage != null
                ? await RenderHandoffAsync(harborHost, worktreePath, previousStage, captain, token).ConfigureAwait(false)
                : null;
            Dictionary<string, string> templateParams = MissionPromptBuilder.BuildTemplateParams(mission, vessel, captain, null, previousStage, handoff);
            List<MissionPlaybookSnapshot> playbookSnapshots = await LoadMissionPlaybookSnapshotsAsync(mission, token).ConfigureAwait(false);

            string content = "";

            // BuildTemplateParams includes the mandatory persona contract even when the captain
            // has no custom system instructions. Deliver that contract to every generated brief.
            if (!String.IsNullOrWhiteSpace(templateParams["CaptainInstructions"]))
            {
                content += await ResolveSectionAsync("mission.captain_instructions_wrapper", templateParams, token).ConfigureAwait(false);
                content += "\n";
            }

            // Vessel context sections
            if (!String.IsNullOrEmpty(vessel.ProjectContext))
            {
                content += await ResolveSectionAsync("mission.project_context_wrapper", templateParams, token).ConfigureAwait(false);
                content += "\n";
            }

            if (!String.IsNullOrEmpty(vessel.StyleGuide))
            {
                content += await ResolveSectionAsync("mission.code_style_wrapper", templateParams, token).ConfigureAwait(false);
                content += "\n";
            }

            if (vessel.EnableModelContext && !String.IsNullOrEmpty(vessel.ModelContext))
            {
                content += await ResolveSectionAsync("mission.model_context_wrapper", templateParams, token).ConfigureAwait(false);
                content += "\n";
            }

            if (playbookSnapshots.Count > 0)
            {
                templateParams["SelectedPlaybooksMarkdown"] = await RenderSelectedPlaybooksMarkdownAsync(
                    harborHost,
                    worktreePath,
                    mission,
                    playbookSnapshots,
                    token).ConfigureAwait(false);
                content += await ResolveSectionAsync("mission.playbooks_wrapper", templateParams, token).ConfigureAwait(false);
                content += "\n";
            }

            // Inject the vessel's project-profile skills (best-effort) as a Skills section.
            string skillsMarkdown = await ResolveSkillsMarkdownAsync(vessel, token).ConfigureAwait(false);
            if (!String.IsNullOrWhiteSpace(skillsMarkdown))
            {
                content += "## Skills\n\n" + skillsMarkdown + "\n";
            }

            // Mission preamble and metadata -- resolve persona prompt first, then inject into metadata template.
            // Apply the vessel's project-profile persona override (if any) so per-project customization takes effect.
            PersonaOverride? personaOverride = await ResolvePersonaOverrideAsync(vessel, mission.Persona, token).ConfigureAwait(false);
            string? personaTemplateName = await ResolvePersonaTemplateNameAsync(mission, token).ConfigureAwait(false);
            PromptVariantEnum promptVariant = await ResolvePromptVariantAsync(mission, captain, token).ConfigureAwait(false);
            string personaPrompt = await MissionPromptBuilder.ResolvePersonaPromptAsync(mission.Persona, personaTemplateName, promptVariant, templateParams, _PromptTemplates, personaOverride, token).ConfigureAwait(false);
            if (handoff != null)
            {
                // The description's prior-stage placeholder receives the rendered handoff, unless the persona template
                // already carries it through {PreviousStageOutput} (Judge, Test Engineer, Linter): deliver it once.
                bool templateCarriesHandoff = personaPrompt.Contains(handoff.Evidence.Trim(), StringComparison.Ordinal);
                string carriedNote = "The prior stage's handoff artifact is rendered in the role instructions below (prior stage evidence and diff).";
                string block = templateCarriesHandoff ? carriedNote : handoff.Combined();
                // A rework description lists the reviewer's findings, its demands and the output contract (Phase 3 item 2).
                string filled = ReworkPromptBuilder.IsReworkDescription(templateParams["MissionDescription"])
                    ? ReworkPromptBuilder.Compose(templateParams["MissionDescription"], handoff, templateCarriesHandoff ? carriedNote : null)
                    : PipelineHandoffContext.Fill(templateParams["MissionDescription"], block);
                if (!String.Equals(filled, templateParams["MissionDescription"], StringComparison.Ordinal))
                {
                    templateParams["MissionDescription"] = filled;
                    personaPrompt = await MissionPromptBuilder.ResolvePersonaPromptAsync(mission.Persona, personaTemplateName, promptVariant, templateParams, _PromptTemplates, personaOverride, token).ConfigureAwait(false);
                }
            }
            else if (ReworkPromptBuilder.IsReworkDescription(templateParams["MissionDescription"]))
            {
                // No reviewer stage to render: keep the fallback findings line and drop the placeholder.
                templateParams["MissionDescription"] = ReworkPromptBuilder.Compose(templateParams["MissionDescription"], null);
                personaPrompt = await MissionPromptBuilder.ResolvePersonaPromptAsync(mission.Persona, personaTemplateName, promptVariant, templateParams, _PromptTemplates, personaOverride, token).ConfigureAwait(false);
            }

            string personaTemplateLabel = personaOverride != null && personaOverride.Enabled && !String.IsNullOrWhiteSpace(personaOverride.PromptTemplateName)
                ? personaOverride.PromptTemplateName!.Trim()
                : !String.IsNullOrWhiteSpace(personaTemplateName) ? personaTemplateName!.Trim() : MissionPromptBuilder.GetPersonaVariantTemplateName(mission.Persona, promptVariant);
            personaPrompt = EnsureWorkerEnvelopeSection(personaPrompt, mission, personaTemplateLabel, templateParams);
            templateParams["PersonaPrompt"] = personaPrompt;
            content += await ResolveSectionAsync("mission.metadata", templateParams, token).ConfigureAwait(false);
            content += "\n";

            // Mission-mode contract: read-only modes (Audit/Research) get a report-shaped brief that forbids
            // repository changes and states that an empty diff is a successful outcome. Write missions add nothing.
            string missionModeSection = MissionModeContract.BuildBriefSection(mission.Mode);
            if (!String.IsNullOrEmpty(missionModeSection))
            {
                content += missionModeSection;
                content += "\n";
            }

            // Resolved git anchors (start commit, target branch, working branch, recent commits on the paths
            // the mission names, and which subject terms already exist in the tree) so the captain does not
            // burn opening turns deriving them. Best-effort: a git failure degrades to a smaller section.
            string? headCommit = null;
            IReadOnlyList<string>? recentPathCommits = null;
            IReadOnlyList<string>? subjectTermsPresent = null;
            IGitService? anchorGit = harborHost != null ? harborHost.Git : _Git;
            if (anchorGit != null)
            {
                try { headCommit = await anchorGit.GetHeadCommitHashAsync(worktreePath, token).ConfigureAwait(false); }
                catch { headCommit = null; }

                string missionText = (mission.Title ?? "") + "\n" + (mission.Description ?? "");
                try
                {
                    IReadOnlyList<string> namedPaths = GitAnchorInputs.ExtractPaths(missionText);
                    if (namedPaths.Count > 0)
                        recentPathCommits = await anchorGit.GetRecentCommitsForPathsAsync(worktreePath, namedPaths, 3, token).ConfigureAwait(false);
                }
                catch { recentPathCommits = null; }

                try
                {
                    IReadOnlyList<string> terms = GitAnchorInputs.ExtractSubjectTerms(mission.Title, mission.Description);
                    if (terms.Count > 0)
                        subjectTermsPresent = await anchorGit.FindExistingSubjectTermsAsync(worktreePath, terms, token).ConfigureAwait(false);
                }
                catch { subjectTermsPresent = null; }
            }
            string gitAnchors = GitAnchorsFormatter.Render(headCommit, vessel.DefaultBranch, mission.BranchName, recentPathCommits, subjectTermsPresent);
            if (!String.IsNullOrEmpty(gitAnchors))
            {
                content += gitAnchors;
                content += "\n";

                // Persist the resolved anchors on the dock (documented raw-JSON snapshot) so the dashboard
                // and a resuming captain can read them without re-deriving. Best-effort.
                if (!String.IsNullOrEmpty(mission.DockId))
                {
                    try
                    {
                        GitAnchorsSnapshot snapshot = new GitAnchorsSnapshot
                        {
                            StartCommit = headCommit,
                            TargetBranch = vessel.DefaultBranch,
                            WorkingBranch = mission.BranchName,
                            RecentPathCommits = recentPathCommits != null ? new List<string>(recentPathCommits) : new List<string>(),
                            SubjectTermsPresent = subjectTermsPresent != null ? new List<string>(subjectTermsPresent) : new List<string>(),
                        };
                        Dock? anchorDock = await _Database.Docks.ReadAsync(mission.DockId!, token).ConfigureAwait(false);
                        if (anchorDock != null)
                        {
                            // The start commit was recorded at provision; a later render (a resume) must not move it.
                            snapshot.StartCommit = DockStartCommit.FromDock(anchorDock) ?? snapshot.StartCommit;
                            anchorDock.GitAnchorsJson = System.Text.Json.JsonSerializer.Serialize(snapshot);
                            anchorDock.LastUpdateUtc = DateTime.UtcNow;
                            await _Database.Docks.UpdateAsync(anchorDock, token).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        _Logging.Debug(_Header + "could not persist git anchors on dock " + mission.DockId + ": " + ex.Message);
                    }
                }
            }

            // Rules, context conservation, merge conflicts, progress signals -- from templates or hardcoded fallback
            content += await ResolveSectionAsync("mission.rules", templateParams, token).ConfigureAwait(false);
            content += "\n";
            content += await ResolveSectionAsync("mission.context_conservation", templateParams, token).ConfigureAwait(false);
            content += "\n";
            content += await ResolveSectionAsync("mission.merge_conflict_avoidance", templateParams, token).ConfigureAwait(false);
            content += "\n";
            content += await ResolveSectionAsync("mission.progress_signals", templateParams, token).ConfigureAwait(false);

            // Papercuts apply to every mission mode: an audit meets stale docs and dead links exactly as
            // an implementation does. Judge and Architect are excluded -- a judge reports what it finds
            // through its verdict, and an architect emits mission blocks only.
            if (!PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Judge) &&
                !PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Architect))
            {
                content += "\n";
                content += BuildPapercutsSection();
            }

            // Model context updates
            if (vessel.EnableModelContext)
            {
                content += "\n";
                content += await ResolveSectionAsync("mission.model_context_updates", templateParams, token).ConfigureAwait(false);
            }

            // If there's an existing runtime instruction file, preserve it and prepend our instructions
            IDockFileSystem files = harborHost != null ? harborHost.Files : _LocalFiles;
            string? existing = await files.ReadTextAsync(instructionsPath, token).ConfigureAwait(false);

            // A runtime whose instructions file is not the repository's own (MUX.md, CODEX.md, GEMINI.md, CURSOR.md,
            // AGENTS.override.md for pi) would otherwise never see the project's rules: mux loads MUX.md first and skips
            // AGENTS.md and CLAUDE.md, and pi's AGENTS.override.md hides the dock's AGENTS.md.
            // Carry the repository's AGENTS.md, else CLAUDE.md, into the generated file. Codex reads AGENTS.md itself.
            if (existing == null && !String.Equals(instructionsFileName, "CLAUDE.md", StringComparison.Ordinal))
            {
                bool runtimeReadsAgentsMd = captain != null && captain.Runtime == AgentRuntimeEnum.Codex;
                string[] fallbacks = runtimeReadsAgentsMd ? new[] { "CLAUDE.md" } : new[] { "AGENTS.md", "CLAUDE.md" };
                if (runtimeReadsAgentsMd && await files.ReadTextAsync(Path.Combine(worktreePath, "AGENTS.md"), token).ConfigureAwait(false) != null)
                    fallbacks = Array.Empty<string>();
                foreach (string fallback in fallbacks)
                {
                    string? projectInstructions = await files.ReadTextAsync(Path.Combine(worktreePath, fallback), token).ConfigureAwait(false);
                    if (!String.IsNullOrWhiteSpace(projectInstructions))
                    {
                        existing = projectInstructions;
                        _Logging.Debug(_Header + "carrying the repository's " + fallback + " into " + instructionsFileName + " for mission " + mission.Id);
                        break;
                    }
                }
            }

            if (existing != null)
            {
                string sanitizedExisting = SanitizeExistingInstructions(existing);

                if (!String.IsNullOrWhiteSpace(sanitizedExisting))
                {
                    if (!String.Equals(existing, sanitizedExisting, StringComparison.Ordinal))
                    {
                        _Logging.Debug(_Header + "sanitized generated mission sections from existing instructions at " + instructionsPath);
                    }

                    templateParams["ExistingClaudeMd"] = sanitizedExisting;
                    content += await ResolveSectionAsync("mission.existing_instructions_wrapper", templateParams, token).ConfigureAwait(false);
                }
            }

            await files.WriteTextAsync(instructionsPath, content, token).ConfigureAwait(false);

            // Persist a stable copy outside the dock so the dashboard and APIs can still
            // show the generated mission instructions after the worktree is reclaimed.
            try
            {
                string instructionsSnapshotDir = Path.Combine(_Settings.LogDirectory, "instructions");
                Directory.CreateDirectory(instructionsSnapshotDir);
                string snapshotPath = Path.Combine(instructionsSnapshotDir, mission.Id + "." + instructionsFileName);
                await File.WriteAllTextAsync(snapshotPath, content).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not persist mission instructions snapshot for " + mission.Id + ": " + ex.ToString());
            }

            // Ensure the generated instruction file is ignored locally so agents don't commit it.
            // Mission instructions are ephemeral and should not alter tracked repository files.
            bool isGitCheckout = false;
            try
            {
                isGitCheckout = await files.AddGitExcludeEntryAsync(worktreePath, instructionsFileName, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Warn(_Header + "could not update git exclude for " + instructionsFileName + ": " + ex.ToString());
            }

            try
            {
                if (isGitCheckout)
                {
                    await HideTrackedInstructionsFileAsync(harborHost != null ? harborHost.Commands : _LocalCommands, worktreePath, instructionsFileName, token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Warn(_Header + "could not hide tracked " + instructionsFileName + " from git: " + ex.Message);
            }

            _Logging.Debug(_Header + "generated mission instructions at " + instructionsPath);
        }

        private async Task<List<MissionPlaybookSnapshot>> LoadMissionPlaybookSnapshotsAsync(Mission mission, CancellationToken token)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (mission.PlaybookSnapshots != null && mission.PlaybookSnapshots.Count > 0)
                return mission.PlaybookSnapshots;

            if (String.IsNullOrEmpty(mission.Id))
                return new List<MissionPlaybookSnapshot>();

            List<MissionPlaybookSnapshot> snapshots = await _Database.Playbooks
                .GetMissionSnapshotsAsync(mission.Id, token)
                .ConfigureAwait(false);
            mission.PlaybookSnapshots = snapshots;
            return snapshots;
        }

        private async Task<string> RenderSelectedPlaybooksMarkdownAsync(
            DockHost? harborHost,
            string worktreePath,
            Mission mission,
            List<MissionPlaybookSnapshot> snapshots,
            CancellationToken token)
        {
            if (String.IsNullOrEmpty(worktreePath)) throw new ArgumentNullException(nameof(worktreePath));
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (snapshots == null || snapshots.Count == 0) return String.Empty;

            List<string> sections = new List<string>();
            bool snapshotStateChanged = false;

            for (int i = 0; i < snapshots.Count; i++)
            {
                MissionPlaybookSnapshot snapshot = snapshots[i];
                string header = "### " + snapshot.FileName;
                string? description = String.IsNullOrWhiteSpace(snapshot.Description) ? null : snapshot.Description.Trim();

                // A captain on a Harbor cannot read the Admiral's files, so a by-reference playbook is attached into its
                // dock instead.
                PlaybookDeliveryModeEnum deliveryMode = snapshot.DeliveryMode;
                if (harborHost != null && deliveryMode == PlaybookDeliveryModeEnum.InstructionWithReference)
                    deliveryMode = PlaybookDeliveryModeEnum.AttachIntoWorktree;

                switch (deliveryMode)
                {
                    case PlaybookDeliveryModeEnum.InstructionWithReference:
                        string resolvedPath = await MaterializeReferencePlaybookAsync(mission, snapshot, i, token).ConfigureAwait(false);
                        if (!String.Equals(snapshot.ResolvedPath, resolvedPath, StringComparison.Ordinal))
                        {
                            snapshot.ResolvedPath = resolvedPath;
                            snapshot.WorktreeRelativePath = null;
                            snapshotStateChanged = true;
                        }

                        sections.Add(
                            header + "\n" +
                            (description != null ? description + "\n" : "") +
                            "Read and follow this playbook at `" + resolvedPath + "`.");
                        break;

                    case PlaybookDeliveryModeEnum.AttachIntoWorktree:
                        WorktreePlaybookLocation attachedPlaybook = await MaterializeWorktreePlaybookAsync(
                            harborHost != null ? harborHost.Files : _LocalFiles,
                            worktreePath,
                            snapshot,
                            i,
                            token).ConfigureAwait(false);
                        if (!String.Equals(snapshot.ResolvedPath, attachedPlaybook.ResolvedPath, StringComparison.Ordinal) ||
                            !String.Equals(snapshot.WorktreeRelativePath, attachedPlaybook.RelativePath, StringComparison.Ordinal))
                        {
                            snapshot.ResolvedPath = attachedPlaybook.ResolvedPath;
                            snapshot.WorktreeRelativePath = attachedPlaybook.RelativePath;
                            snapshotStateChanged = true;
                        }

                        sections.Add(
                            header + "\n" +
                            (description != null ? description + "\n" : "") +
                            "Read and follow this attached playbook at `" + attachedPlaybook.RelativePath.Replace("\\", "/") + "`.");
                        break;

                    default:
                        sections.Add(
                            header + "\n" +
                            (description != null ? description + "\n\n" : "") +
                            snapshot.Content.TrimEnd());
                        break;
                }
            }

            if (snapshotStateChanged)
            {
                await _Database.Playbooks.SetMissionSnapshotsAsync(mission.Id, snapshots, token).ConfigureAwait(false);
            }

            return String.Join("\n\n", sections);
        }

        private async Task<string> MaterializeReferencePlaybookAsync(
            Mission mission,
            MissionPlaybookSnapshot snapshot,
            int selectionOrder,
            CancellationToken token)
        {
            string playbookDir = Path.Combine(_Settings.LogDirectory, "playbooks", mission.Id);
            Directory.CreateDirectory(playbookDir);

            string fileName = BuildMaterializedPlaybookFileName(selectionOrder, snapshot.FileName);
            string resolvedPath = Path.Combine(playbookDir, fileName);
            await File.WriteAllTextAsync(resolvedPath, snapshot.Content, token).ConfigureAwait(false);
            return resolvedPath;
        }

        private async Task<WorktreePlaybookLocation> MaterializeWorktreePlaybookAsync(
            IDockFileSystem files,
            string worktreePath,
            MissionPlaybookSnapshot snapshot,
            int selectionOrder,
            CancellationToken token)
        {
            string relativeDir = Path.Combine(".armada", "playbooks");
            string absoluteDir = Path.Combine(worktreePath, relativeDir);

            string fileName = BuildMaterializedPlaybookFileName(selectionOrder, snapshot.FileName);
            string absolutePath = Path.Combine(absoluteDir, fileName);
            await files.WriteTextAsync(absolutePath, snapshot.Content, token).ConfigureAwait(false);

            await files.AddGitExcludeEntryAsync(worktreePath, ".armada/playbooks/", token).ConfigureAwait(false);

            return new WorktreePlaybookLocation
            {
                ResolvedPath = absolutePath,
                RelativePath = Path.Combine(relativeDir, fileName)
            };
        }

        private static string BuildMaterializedPlaybookFileName(int selectionOrder, string? originalFileName)
        {
            string safeName = String.IsNullOrWhiteSpace(originalFileName) ? "PLAYBOOK.md" : originalFileName.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                safeName = safeName.Replace(invalid, '_');
            }

            return (selectionOrder + 1).ToString("D2") + "_" + safeName;
        }

        /// <summary>
        /// Resolve the persona prompt variant for the mission (<see cref="PromptVariantResolver"/>): the voyage's captain
        /// override for the mission's persona, then the captain's <c>promptVariant</c> runtime option, then Current.
        /// Best-effort: a voyage read failure falls back to the captain's setting.
        /// </summary>
        private async Task<PromptVariantEnum> ResolvePromptVariantAsync(Mission mission, Captain? captain, CancellationToken token)
        {
            List<CaptainAssignmentOverride> overrides;
            try
            {
                overrides = await ReadVoyageCaptainOverridesAsync(mission.VoyageId, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _Logging.Debug(_Header + "could not read voyage overrides for the prompt variant of mission " + mission.Id + ": " + ex.Message);
                overrides = new List<CaptainAssignmentOverride>();
            }

            PromptVariantEnum variant = PromptVariantResolver.Resolve(mission.Persona, overrides, captain);
            if (variant != PromptVariantEnum.Current)
                _Logging.Debug(_Header + "mission " + mission.Id + " renders the " + variant + " persona prompt variant");
            return variant;
        }

        /// <summary>
        /// Resolve the prompt template configured on the mission's persona (<see cref="Persona.PromptTemplateName"/>).
        /// Returns null (use the conventional persona.&lt;name&gt; template) when the persona is not found or names no
        /// template, and logs and returns null when the configured template does not exist.
        /// </summary>
        private async Task<string?> ResolvePersonaTemplateNameAsync(Mission mission, CancellationToken token)
        {
            string personaName = PersonaCatalog.NormalizeName(mission.Persona ?? PersonaCatalog.Worker);
            if (String.IsNullOrWhiteSpace(personaName)) return null;

            Persona? persona;
            try
            {
                persona = await ReadPersonaByNameAsync(mission.TenantId, personaName, token, mission.UserId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not read persona " + personaName + " for mission " + mission.Id + "; using the default persona template: " + ex.Message);
                return null;
            }

            if (persona == null || String.IsNullOrWhiteSpace(persona.PromptTemplateName)) return null;
            string templateName = persona.PromptTemplateName.Trim();
            if (_PromptTemplates == null) return templateName;

            PromptTemplate? template = await _PromptTemplates.ResolveAsync(templateName, token).ConfigureAwait(false);
            if (template == null)
            {
                _Logging.Warn(_Header + "persona " + persona.Name + " references prompt template " + templateName +
                    ", which does not exist; mission " + mission.Id + " uses the default template " + MissionPromptBuilder.GetPersonaTemplateName(personaName));
                return null;
            }

            return templateName;
        }

        /// <summary>
        /// Render the prior stage's handoff artifact for the captain about to read it: the budget comes from the captain's
        /// served context when known, else <see cref="ArmadaSettings.HandoffBudgetTokens"/>; the pointer to the complete
        /// artifact uses a tool the captain has. A captain that can only read files gets the complete artifact written to
        /// .armada/handoff/ in its worktree (git-excluded and a scratch path, so it is never committed). If that write
        /// fails, the rendering says that nothing beyond it can reach the captain.
        /// </summary>
        private async Task<HandoffRendering> RenderHandoffAsync(DockHost? harborHost, string worktreePath, Mission previousStage, Captain? captain, CancellationToken token)
        {
            HandoffArtifact artifact = await new HandoffArtifactService(_Database, _Logging).GetOrCreateForStageAsync(previousStage, token).ConfigureAwait(false);
            ModelEndpoint? endpoint = null;
            if (!String.IsNullOrEmpty(captain?.ModelEndpointId))
            {
                try { endpoint = await _Database.ModelEndpoints.ReadAsync(captain!.ModelEndpointId!, token).ConfigureAwait(false); }
                catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested)) { endpoint = null; }
            }

            HandoffReader reader = HandoffReaderResolver.Resolve(captain, endpoint);
            HandoffRendering rendering = HandoffRenderer.Render(artifact, reader, _Settings.HandoffBudgetTokens);
            if (rendering.Pointer == HandoffPointerKindEnum.File && rendering.FilePath != null)
            {
                IDockFileSystem files = harborHost != null ? harborHost.Files : _LocalFiles;
                try
                {
                    string absolute = Path.Combine(worktreePath, rendering.FilePath.Replace('/', Path.DirectorySeparatorChar));
                    await files.WriteTextAsync(absolute, HandoffRenderer.RenderFullDocument(artifact), token).ConfigureAwait(false);
                    await files.AddGitExcludeEntryAsync(worktreePath, HandoffRenderer.HandoffDirectory + "/", token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                {
                    _Logging.Warn(_Header + "could not write the handoff file for mission " + previousStage.Id + " into " + worktreePath + ": " + ex.Message);
                    HandoffReader noAccess = new HandoffReader { ContextTokens = reader.ContextTokens, HasMcpTools = false, CanReadFiles = false };
                    rendering = HandoffRenderer.RenderWithBudget(artifact, noAccess, rendering.BudgetTokens);
                }
            }

            return rendering;
        }

        /// <summary>
        /// Read the prior pipeline stage (the mission this one depends on), or null. Best-effort.
        /// </summary>
        private async Task<Mission?> ReadPreviousStageAsync(Mission mission, CancellationToken token)
        {
            if (String.IsNullOrEmpty(mission.DependsOnMissionId)) return null;
            try
            {
                return !String.IsNullOrEmpty(mission.TenantId)
                    ? await _Database.Missions.ReadAsync(mission.TenantId!, mission.DependsOnMissionId!, token).ConfigureAwait(false)
                    : await _Database.Missions.ReadAsync(mission.DependsOnMissionId!, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not read prior stage " + mission.DependsOnMissionId + " for mission " + mission.Id + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Resolve the effective per-project persona override for a mission's persona by selecting the
        /// vessel's project profile (vessel -> fleet -> global). Best-effort: returns null on any error so
        /// a profile lookup never blocks dispatch.
        /// </summary>
        private async Task<PersonaOverride?> ResolvePersonaOverrideAsync(Vessel vessel, string? persona, CancellationToken token)
        {
            if (vessel == null || String.IsNullOrWhiteSpace(persona)) return null;

            try
            {
                List<ProjectProfile> profiles = await _Database.ProjectProfiles.EnumerateAllAsync(
                    new ProjectProfileQuery
                    {
                        TenantId = vessel.TenantId,
                        Active = true,
                        PageNumber = 1,
                        PageSize = 1000
                    },
                    token).ConfigureAwait(false);

                if (profiles.Count == 0) return null;

                ProjectProfile? profile = ProjectProfileService.SelectForVessel(profiles, vessel);
                return ProjectProfileService.ResolvePersonaOverride(profile, persona);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error resolving persona override for vessel " + vessel.Id + ": " + ex.ToString());
                return null;
            }
        }

        /// <summary>
        /// Resolve the vessel's project-profile skills into a combined markdown block for prompt
        /// injection. Best-effort: returns an empty string on any error or when no profile/skills apply.
        /// Skill references in the profile may be skill ids (skl_...) or skill names.
        /// </summary>
        private async Task<string> ResolveSkillsMarkdownAsync(Vessel vessel, CancellationToken token)
        {
            if (vessel == null) return String.Empty;

            try
            {
                List<ProjectProfile> profiles = await _Database.ProjectProfiles.EnumerateAllAsync(
                    new ProjectProfileQuery { TenantId = vessel.TenantId, Active = true, PageNumber = 1, PageSize = 1000 },
                    token).ConfigureAwait(false);
                if (profiles.Count == 0) return String.Empty;

                ProjectProfile? profile = ProjectProfileService.SelectForVessel(profiles, vessel);
                if (profile == null || profile.Skills == null || profile.Skills.Count == 0) return String.Empty;

                List<Skill> skills = await _Database.Skills.EnumerateAllAsync(
                    new SkillQuery { TenantId = vessel.TenantId, Active = true, PageNumber = 1, PageSize = 1000 },
                    token).ConfigureAwait(false);
                if (skills.Count == 0) return String.Empty;

                Dictionary<string, Skill> byId = new Dictionary<string, Skill>(StringComparer.Ordinal);
                Dictionary<string, Skill> byName = new Dictionary<string, Skill>(StringComparer.OrdinalIgnoreCase);
                foreach (Skill skill in skills)
                {
                    byId[skill.Id] = skill;
                    if (!String.IsNullOrWhiteSpace(skill.Name)) byName[skill.Name.Trim()] = skill;
                }

                List<string> blocks = new List<string>();
                foreach (string reference in profile.Skills)
                {
                    if (String.IsNullOrWhiteSpace(reference)) continue;
                    Skill? resolved = null;
                    if (byId.TryGetValue(reference.Trim(), out Skill? byIdMatch)) resolved = byIdMatch;
                    else if (byName.TryGetValue(reference.Trim(), out Skill? byNameMatch)) resolved = byNameMatch;
                    if (resolved == null || String.IsNullOrWhiteSpace(resolved.Content)) continue;
                    blocks.Add("### " + resolved.Name + "\n\n" + resolved.Content.Trim());
                }

                return String.Join("\n\n", blocks);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error resolving skills for vessel " + vessel.Id + ": " + ex.ToString());
                return String.Empty;
            }
        }

        /// <summary>
        /// Preserve only stable project instructions from an existing runtime instruction file.
        /// Generated Armada mission blocks are stripped to avoid recursively injecting stale
        /// mission objectives into future captain prompts.
        /// </summary>
        private static string SanitizeExistingInstructions(string existing)
        {
            if (String.IsNullOrWhiteSpace(existing)) return String.Empty;

            int generatedSectionIndex = existing.IndexOf("# Mission Instructions", StringComparison.Ordinal);
            if (generatedSectionIndex < 0)
            {
                generatedSectionIndex = existing.IndexOf("## Mission Instructions", StringComparison.Ordinal);
            }

            if (generatedSectionIndex >= 0)
            {
                return existing.Substring(0, generatedSectionIndex).TrimEnd();
            }

            return existing.TrimEnd();
        }

        /// <summary>
        /// Builds the papercut directive. A captain that meets broken friction -- a stale doc, a dead
        /// link, a brief that contradicts itself, a missing sibling repository -- works around it and
        /// says nothing, so the next captain on the same vessel pays the same cost again. This asks for
        /// one line per problem and forbids fixing it out of scope, which keeps the report cheap and the
        /// diff clean.
        ///
        /// Built in code rather than added to a template, so an existing stored template row that
        /// predates this module cannot silently drop it.
        /// </summary>
        /// <returns>The papercut section.</returns>
        internal static string BuildPapercutsSection()
        {
            return
                "## Papercuts\n" +
                "\n" +
                "Report friction you meet. Do not work around it silently. One line each, on its own line:\n" +
                "\n" +
                "`[ARMADA:PAPERCUT] {\"category\":\"MissingDoc\",\"severity\":\"Low\",\"title\":\"one line\",\"detail\":\"optional\",\"path\":\"optional/file.cs\"}`\n" +
                "\n" +
                "- Categories: BriefContradiction, ToolFailure, MissingDoc, BrokenLink, RepoFriction, " +
                "TestFlake, EnvSetup, PlatformBug, Other. Severity: Low, Medium, High.\n" +
                "- Report it, do not fix it. A fix outside your mission scope is a merge conflict for another captain.\n" +
                "- Never block on a papercut. Report it and continue the mission.\n" +
                "- Your own assigned work is not a papercut. Report what made the work harder than it needed to be.\n" +
                "- Include no credentials, tokens, or absolute host paths.\n" +
                "- Ten per mission is the limit. Report the ones that cost you time.\n";
        }

        /// <summary>
        /// Resolve a named template section. Falls back to empty string if no template service or template not found.
        /// </summary>
        private async Task<string> ResolveSectionAsync(string templateName, Dictionary<string, string> templateParams, CancellationToken token)
        {
            if (_PromptTemplates != null)
            {
                string rendered = await _PromptTemplates.RenderAsync(templateName, templateParams, token).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(rendered))
                    return rendered;
            }

            // Hardcoded fallbacks for backward compatibility when template service is unavailable
            string fallback = GetHardcodedFallback(templateName);
            if (!String.IsNullOrEmpty(fallback))
            {
                foreach (KeyValuePair<string, string> kvp in templateParams)
                {
                    fallback = fallback.Replace("{" + kvp.Key + "}", kvp.Value ?? "");
                }
            }
            return fallback;
        }

        /// <summary>
        /// Returns hardcoded prompt section content as a fallback when the template service is unavailable.
        /// </summary>
        private string GetHardcodedFallback(string templateName)
        {
            switch (templateName)
            {
                case "mission.rules":
                    return
                        "## Rules\n" +
                        "- Work only within this worktree directory\n" +
                        "- Commit all changes to the current branch\n" +
                        "- Commit and push your changes -- the Admiral will also push if needed\n" +
                        "- If you encounter a blocking issue, commit what you have and exit\n" +
                        "- Exit with code 0 on success\n" +
                        "- Do not use extended/Unicode characters (em dashes, smart quotes, etc.) -- use only ASCII characters in all output and commit messages\n" +
                        "- Do not use ANSI color codes or terminal formatting in output -- keep all output plain text\n";

                case "mission.context_conservation":
                    return
                        "## Context Conservation (CRITICAL)\n" +
                        "\n" +
                        "You have a limited context window. Exceeding it will crash your process and fail the mission. " +
                        "Follow these rules to stay within limits:\n" +
                        "\n" +
                        "1. **NEVER read entire large files.** If a file is over 200 lines, read only the specific " +
                        "section you need using line offsets. Use grep/search to find the right section first.\n" +
                        "\n" +
                        "2. **Read before you write, but read surgically.** Read only the 10-30 lines around the code " +
                        "you need to change, not the whole file.\n" +
                        "\n" +
                        "3. **Do not explore the codebase broadly.** Only read files explicitly mentioned in your " +
                        "mission description. If the mission says to edit README.md, read only the section you need " +
                        "to edit, not the entire README.\n" +
                        "\n" +
                        "4. **Make your changes and finish.** Do not re-read files to verify your changes, do not " +
                        "read files for 'context' that isn't directly needed for your edit, and do not explore related " +
                        "files out of curiosity.\n" +
                        "\n" +
                        "5. **Complete the assigned scope.** Work incrementally and validate relevant changes. If a real " +
                        "blocker prevents completion, preserve your work and report the blocker explicitly. Never " +
                        "emit a success marker or claim completion for partial work.\n";

                case "mission.merge_conflict_avoidance":
                    return
                        "## Avoiding Merge Conflicts (CRITICAL)\n" +
                        "\n" +
                        "You are one of several captains working on this repository. Other captains may be working on " +
                        "other missions in parallel on separate branches. To prevent merge conflicts and landing failures, " +
                        "you MUST follow these rules:\n" +
                        "\n" +
                        "1. **Only modify files explicitly mentioned in your mission description.** If the description says " +
                        "to edit `src/routes/users.ts`, do NOT also refactor `src/routes/orders.ts` even if you notice " +
                        "improvements. Another captain may be working on that file.\n" +
                        "\n" +
                        "2. **Do not make \"helpful\" changes outside your scope.** Do not rename shared variables, " +
                        "reorganize imports in files you were not asked to touch, reformat code in unrelated files, " +
                        "update documentation files unless instructed, or modify configuration/project files " +
                        "(e.g., .csproj, package.json, tsconfig.json) unless your mission specifically requires it.\n" +
                        "\n" +
                        "3. **Do not modify barrel/index export files** (e.g., index.ts, mod.rs) unless your mission " +
                        "explicitly requires it. These are high-conflict files that many missions may need to touch.\n" +
                        "\n" +
                        "4. **Keep changes minimal and focused.** The fewer files you touch, the lower the risk of " +
                        "conflicts. If your mission can be completed by editing 2 files, do not edit 5.\n" +
                        "\n" +
                        "5. **If you must create new files**, prefer names that are specific to your mission's feature " +
                        "rather than generic names that another captain might also choose.\n" +
                        "\n" +
                        "6. **Do not modify or delete files created by another mission's branch.** You are working in " +
                        "an isolated worktree -- if you see files that seem unrelated to your mission, leave them alone.\n" +
                        "\n" +
                        "Violating these rules will cause your branch to conflict with other captains' branches during " +
                        "landing, resulting in a LandingFailed status and wasted work.\n";

                case "mission.progress_signals":
                    return
                        "## Progress Signals (Optional)\n" +
                        "You can report progress to the Admiral by printing these lines to stdout:\n" +
                        "- `[ARMADA:PROGRESS] 50` -- report completion percentage (0-100)\n" +
                        "- `[ARMADA:STATUS] Testing` -- report that you are now running tests (informational; `[ARMADA:STATUS] InProgress` switches back)\n" +
                        "- `[ARMADA:MESSAGE] your message here` -- send a progress message\n" +
                        "- `[ARMADA:TOKENS] input=1234 output=567 cached=0` -- report the tokens you consumed this session (input/prompt, output/completion, and cache-read). Emit this once near the end if your runtime can determine the counts; it lets the Admiral record real token usage instead of an estimate.\n" +
                        "- `[ARMADA:RESULT] COMPLETE` -- worker/test engineer mission finished successfully\n" +
                        CaptainBlockedContract.SignalLine +
                        "- `[ARMADA:VERDICT] PASS` -- judge approves the mission\n" +
                        "- `[ARMADA:VERDICT] FAIL` -- judge rejects the mission\n" +
                        "- `[ARMADA:VERDICT] NEEDS_REVISION` -- judge requests follow-up changes\n" +
                        "Architect missions must not emit `[ARMADA:RESULT]` or `[ARMADA:VERDICT]`; they must output only real mission definitions: one fenced `armada-plan` JSON block (preferred) or real `[ARMADA:MISSION]` blocks.\n";

                case "mission.model_context_updates":
                    return
                        "## Model Context Updates\n" +
                        "\n" +
                        "Model context accumulation is enabled for this vessel. Before you finish your mission, " +
                        "review the existing model context above (if any) and consider whether you have discovered " +
                        "key information that would help future agents work on this repository more effectively. " +
                        "Examples include: architectural insights, code style conventions, naming conventions, " +
                        "logging patterns, error handling patterns, testing patterns, build quirks, common pitfalls, " +
                        "important dependencies, interdependencies between modules, concurrency patterns, " +
                        "and performance considerations.\n" +
                        "\n" +
                        "If you have useful additions, call `update_vessel_context` with the `modelContext` " +
                        "parameter set to the COMPLETE updated model context (not just your additions -- include " +
                        "the existing content with your additions merged in). Be thorough -- this context is a " +
                        "goldmine for future agents. Focus on information that is not obvious from reading the code, " +
                        "and organize it clearly with sections or headings.\n" +
                        "\n" +
                        "If you have nothing to add, skip this step.\n";

                case "mission.playbooks_wrapper":
                    return
                        "## Playbooks\n" +
                        "These playbooks are part of the required instructions for this mission. Read and follow them.\n" +
                        "\n" +
                        "{SelectedPlaybooksMarkdown}\n";

                case "mission.captain_instructions_wrapper":
                    return
                        "## Captain Instructions\n" +
                        "{CaptainInstructions}\n";

                case "mission.project_context_wrapper":
                    return
                        "## Project Context\n" +
                        "{ProjectContext}\n";

                case "mission.code_style_wrapper":
                    return
                        "## Code Style\n" +
                        "{StyleGuide}\n";

                case "mission.model_context_wrapper":
                    return
                        "## Model Context\n" +
                        "The following context was accumulated by AI agents during previous missions on this repository. " +
                        "Use this information to work more effectively.\n" +
                        "\n" +
                        "{ModelContext}\n";

                case "mission.metadata":
                    return
                        "# Mission Instructions\n" +
                        "\n" +
                        "{PersonaPrompt}\n" +
                        "\n" +
                        "## Mission\n" +
                        "- **Title:** {MissionTitle}\n" +
                        "- **ID:** {MissionId}\n" +
                        "- **Voyage:** {VoyageId}\n" +
                        "\n" +
                        "## Description\n" +
                        "{MissionDescription}\n" +
                        "\n" +
                        "## Repository\n" +
                        "- **Name:** {VesselName}\n" +
                        "- **Branch:** {BranchName}\n" +
                        "- **Default Branch:** {DefaultBranch}\n";

                case "mission.existing_instructions_wrapper":
                    return
                        "\n## Existing Project Instructions\n" +
                        "\n" +
                        "{ExistingClaudeMd}";

                default:
                    return "";
            }
        }

        #endregion
    }
}
