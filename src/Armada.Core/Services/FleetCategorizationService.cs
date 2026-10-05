namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Protocol;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Captain-driven fleet categorization for vessel import batches. For each run it creates a scratch directory under
    /// the Admiral data directory (never a user repository), writes a REPOSITORIES.md manifest describing every imported
    /// vessel, reserves the chosen captain (Idle to Analyzing, atomically), runs it once through
    /// <see cref="ICaptainPromptRunner"/> with the operator's instructions plus an Admiral-owned output contract, then
    /// parses and validates fleet-recommendations.json into structured recommendation rows. Runs as a background job of
    /// kind FleetCategorization that honors the job cancel endpoint and a configurable time limit
    /// (Import.CategorizationTimeoutMinutes). Thread-safe; at most one run per batch at a time.
    /// </summary>
    public class FleetCategorizationService : IFleetCategorizationService
    {
        #region Public-Members

        /// <summary>
        /// Name of the manifest file written to the scratch directory.
        /// </summary>
        public const string ManifestFileName = "REPOSITORIES.md";

        /// <summary>
        /// Name of the file the captain must write.
        /// </summary>
        public const string OutputFileName = "fleet-recommendations.json";

        /// <summary>
        /// Name of the bucket that collects vessels the captain did not assign. Applying never creates a fleet with
        /// this name; its vessels keep their current fleet.
        /// </summary>
        public const string UncategorizedFleetName = "Uncategorized";

        /// <summary>
        /// How often a running categorization checks its job for cancellation and records a heartbeat, in
        /// milliseconds. Default 2000, minimum 50, maximum 60000.
        /// </summary>
        public int JobPollIntervalMs
        {
            get => _JobPollIntervalMs;
            set => _JobPollIntervalMs = Math.Clamp(value, 50, 60000);
        }

        /// <summary>
        /// README lines copied into the manifest for each repository. Default 60, minimum 0, maximum 500.
        /// </summary>
        public int ReadmeLineLimit
        {
            get => _ReadmeLineLimit;
            set => _ReadmeLineLimit = Math.Clamp(value, 0, 500);
        }

        /// <summary>
        /// Maximum number of fleets accepted from a captain; extra fleets are dropped with a warning. Default 50,
        /// minimum 1, maximum 500.
        /// </summary>
        public int MaxFleets
        {
            get => _MaxFleets;
            set => _MaxFleets = Math.Clamp(value, 1, 500);
        }

        /// <summary>
        /// Optional override for the run time limit; when null, Import.CategorizationTimeoutMinutes applies. Intended
        /// for tests. Values below one second are raised to one second.
        /// </summary>
        public TimeSpan? TimeoutOverride
        {
            get => _TimeoutOverride;
            set => _TimeoutOverride = value.HasValue && value.Value < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : value;
        }

        /// <inheritdoc />
        public int TimeoutMinutes => (int)Math.Ceiling(ResolveTimeout().TotalMinutes);

        /// <summary>
        /// True to keep the scratch directory after a successful run (it is always kept after a failure, for
        /// diagnosis). Default false.
        /// </summary>
        public bool KeepWorkingDirectory { get; set; } = false;

        #endregion

        #region Private-Members

        private readonly string _Header = "[FleetCategorizationService] ";
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly JobService _Jobs;
        private readonly ICaptainPromptRunner _Runner;
        private readonly IPromptTemplateService? _Templates;
        private readonly LoggingModule _Logging;
        private readonly HashSet<string> _ActiveBatches = new HashSet<string>(StringComparer.Ordinal);
        private readonly object _ActiveLock = new object();
        private int _JobPollIntervalMs = 2000;
        private int _ReadmeLineLimit = 60;
        private int _MaxFleets = 50;
        private TimeSpan? _TimeoutOverride = null;

        private static readonly JsonSerializerOptions _ParseOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        private static readonly JsonSerializerOptions _ResultJsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private static readonly string[] _ReadmeNames = new string[] { "README.md", "README.markdown", "README.rst", "README.txt", "README", "readme.md" };

        private static readonly Dictionary<string, string> _ManifestLanguages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "package.json", "JavaScript/TypeScript (npm)" },
            { "tsconfig.json", "TypeScript" },
            { "pyproject.toml", "Python" },
            { "setup.py", "Python" },
            { "requirements.txt", "Python" },
            { "Pipfile", "Python" },
            { "go.mod", "Go" },
            { "Cargo.toml", "Rust" },
            { "pom.xml", "Java (Maven)" },
            { "build.gradle", "Java/Kotlin (Gradle)" },
            { "build.gradle.kts", "Kotlin (Gradle)" },
            { "Gemfile", "Ruby" },
            { "composer.json", "PHP" },
            { "mix.exs", "Elixir" },
            { "Package.swift", "Swift" },
            { "pubspec.yaml", "Dart/Flutter" },
            { "CMakeLists.txt", "C/C++ (CMake)" },
            { "Makefile", "Make" },
            { "Dockerfile", "Docker" },
            { "docker-compose.yml", "Docker Compose" },
            { "terraform.tf", "Terraform" },
            { "main.tf", "Terraform" }
        };

        private static readonly Dictionary<string, string> _ManifestExtensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".sln", "C#/.NET (solution)" },
            { ".slnx", "C#/.NET (solution)" },
            { ".csproj", "C#/.NET" },
            { ".fsproj", "F#/.NET" },
            { ".vbproj", "VB/.NET" },
            { ".tf", "Terraform" }
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Application settings; DataDirectory and Import are read live.</param>
        /// <param name="jobs">Job service.</param>
        /// <param name="runner">Runs a captain once on a prompt.</param>
        /// <param name="templates">Prompt template service for the default instructions, or null to use the built-in text.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when database, settings, jobs, runner, or logging is null.</exception>
        public FleetCategorizationService(
            DatabaseDriver database,
            ArmadaSettings settings,
            JobService jobs,
            ICaptainPromptRunner runner,
            IPromptTemplateService? templates,
            LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            _Runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _Templates = templates;
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task ValidateRequestAsync(string tenantId, VesselImportCategorizationRequest? request, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (request == null || !request.Enabled) return;
            if (String.IsNullOrWhiteSpace(request.CaptainId))
                throw new VesselImportRequestException(VesselImportCodes.CategorizationCaptainRequired, "categorization.captainId is required when categorization is enabled.", nameof(request));

            Captain? captain = await _Database.Captains.ReadAsync(tenantId, request.CaptainId.Trim(), token).ConfigureAwait(false);
            if (captain == null) throw new VesselImportRequestException(VesselImportCodes.CategorizationCaptainNotFound, "Captain not found: " + request.CaptainId, nameof(request));
        }

        /// <inheritdoc />
        public async Task<string> GetDefaultPromptAsync(CancellationToken token = default)
        {
            if (_Templates != null)
            {
                PromptTemplate? template = await _Templates.ResolveAsync(PromptTemplateService.FleetCategorizationTemplateName, token).ConfigureAwait(false);
                if (template != null && !String.IsNullOrWhiteSpace(template.Content)) return template.Content;
            }

            return "Look at all of the repositories listed in REPOSITORIES.md, figure out what each one does, then group them "
                + "into a small set of recommended fleets of related repositories. Prefer 3-10 fleets with short, descriptive names.";
        }

        /// <inheritdoc />
        public async Task<VesselImportBatch> ScheduleAsync(VesselImportBatch batch, VesselImportCategorizationRequest request, CancellationToken token = default)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrEmpty(batch.TenantId)) throw new ArgumentException("Batch tenant is required.", nameof(batch));

            string prompt = String.IsNullOrWhiteSpace(request.Prompt) ? await GetDefaultPromptAsync(token).ConfigureAwait(false) : request.Prompt!;

            batch.CategorizationStatus = VesselImportCategorizationStatusEnum.Pending;
            batch.CategorizationCaptainId = request.CaptainId?.Trim();
            batch.CategorizationPrompt = prompt;
            batch.CategorizationApplyAutomatically = request.ApplyAutomatically;
            batch.CategorizationError = null;
            batch.CategorizationJobId = null;
            batch.CategorizationStartedUtc = null;
            batch.CategorizationCompletedUtc = null;
            await _Database.VesselImportFleetRecommendations.DeleteByBatchAsync(batch.TenantId!, batch.Id, token).ConfigureAwait(false);
            VesselImportBatch updated = await _Database.VesselImportBatches.UpdateAsync(batch, token).ConfigureAwait(false);
            batch.LastUpdateUtc = updated.LastUpdateUtc;
            return batch;
        }

        /// <inheritdoc />
        public async Task<VesselImportBatch> StartAsync(string tenantId, string batchId, string? userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(batchId)) throw new ArgumentNullException(nameof(batchId));

            VesselImportBatch? batch = await _Database.VesselImportBatches.ReadAsync(tenantId, batchId, token).ConfigureAwait(false);
            if (batch == null) throw new KeyNotFoundException("Import batch not found: " + batchId);

            lock (_ActiveLock)
            {
                if (!_ActiveBatches.Add(batch.Id)) throw new InvalidOperationException("Fleet categorization for batch " + batch.Id + " is already running.");
            }

            bool launched = false;
            try
            {
                List<Vessel> vessels = await ResolveBatchVesselsAsync(tenantId, batch.Id, token).ConfigureAwait(false);
                if (vessels.Count == 0)
                {
                    await FailBatchAsync(batch, "No imported vessels to categorize. Import at least one repository first.").ConfigureAwait(false);
                    return batch;
                }

                Job job = await _Jobs.EnqueueAsync(
                    "Fleet categorization for import " + batch.Id + " (" + vessels.Count + " vessels)",
                    JobKindEnum.FleetCategorization, tenantId, userId, token).ConfigureAwait(false);

                batch.CategorizationStatus = VesselImportCategorizationStatusEnum.Pending;
                batch.CategorizationJobId = job.Id;
                batch.CategorizationError = null;
                batch.CategorizationCompletedUtc = null;
                VesselImportBatch updated = await _Database.VesselImportBatches.UpdateAsync(batch, token).ConfigureAwait(false);
                batch.LastUpdateUtc = updated.LastUpdateUtc;

                VesselImportBatch backgroundBatch = batch;
                launched = true;
                _ = Task.Run(() => RunJobAsync(job, backgroundBatch, vessels, userId));
                return batch;
            }
            finally
            {
                if (!launched) ReleaseBatch(batch.Id);
            }
        }

        /// <inheritdoc />
        public async Task<VesselImportBatch> CategorizeAsync(string tenantId, string batchId, string? userId, VesselImportCategorizationRequest? request, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(batchId)) throw new ArgumentNullException(nameof(batchId));

            VesselImportBatch? batch = await _Database.VesselImportBatches.ReadAsync(tenantId, batchId, token).ConfigureAwait(false);
            if (batch == null) throw new KeyNotFoundException("Import batch not found: " + batchId);

            if (batch.Status != VesselImportBatchStatusEnum.Completed && batch.Status != VesselImportBatchStatusEnum.CompletedWithFailures)
                throw new InvalidOperationException("Fleet categorization needs a finished import; batch " + batch.Id + " is " + batch.Status + ".");
            if (IsCategorizationActive(batch))
                throw new InvalidOperationException("Fleet categorization for batch " + batch.Id + " is already " + batch.CategorizationStatus + ".");

            VesselImportCategorizationRequest effective = new VesselImportCategorizationRequest();
            effective.Enabled = true;
            effective.CaptainId = !String.IsNullOrWhiteSpace(request?.CaptainId) ? request!.CaptainId : batch.CategorizationCaptainId;
            effective.Prompt = !String.IsNullOrWhiteSpace(request?.Prompt) ? request!.Prompt : batch.CategorizationPrompt;
            effective.ApplyAutomatically = request != null ? request.ApplyAutomatically : batch.CategorizationApplyAutomatically;
            await ValidateRequestAsync(tenantId, effective, token).ConfigureAwait(false);

            await ScheduleAsync(batch, effective, token).ConfigureAwait(false);
            return await StartAsync(tenantId, batch.Id, userId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<FleetRecommendationApplyResult> ApplyAsync(string tenantId, string batchId, string? userId, FleetRecommendationApplyRequest request, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(batchId)) throw new ArgumentNullException(nameof(batchId));
            if (request == null) throw new ArgumentNullException(nameof(request));

            VesselImportBatch? batch = await _Database.VesselImportBatches.ReadAsync(tenantId, batchId, token).ConfigureAwait(false);
            if (batch == null) throw new KeyNotFoundException("Import batch not found: " + batchId);
            if (batch.Status == VesselImportBatchStatusEnum.Discovering || batch.Status == VesselImportBatchStatusEnum.Importing)
                throw new InvalidOperationException("Batch " + batch.Id + " is still " + batch.Status + "; apply fleets after it finishes.");
            if (IsCategorizationActive(batch))
                throw new InvalidOperationException("Fleet categorization for batch " + batch.Id + " is still " + batch.CategorizationStatus + "; wait for it to finish or cancel its job.");

            List<Vessel> batchVessels = await ResolveBatchVesselsAsync(tenantId, batch.Id, token).ConfigureAwait(false);
            List<VesselImportFleetRecommendation> previous = await _Database.VesselImportFleetRecommendations.EnumerateByBatchAsync(tenantId, batch.Id, token).ConfigureAwait(false);
            return await ApplyInternalAsync(batch, batchVessels, previous, request, userId, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task RecoverAsync(CancellationToken token = default)
        {
            List<VesselImportBatch> batches = await _Database.VesselImportBatches.EnumerateInProgressAsync(token).ConfigureAwait(false);
            foreach (VesselImportBatch batch in batches)
            {
                if (!IsCategorizationActive(batch)) continue;
                lock (_ActiveLock)
                {
                    if (_ActiveBatches.Contains(batch.Id)) continue;
                }

                _Logging.Warn(_Header + "failing categorization of batch " + batch.Id + " orphaned by an Admiral restart");
                await FailBatchAsync(batch, "The Admiral restarted while fleet categorization was running. Retry categorization.").ConfigureAwait(false);
                if (!String.IsNullOrEmpty(batch.CategorizationJobId)) await FailJobAsync(batch.CategorizationJobId!, "Admiral restarted while the job was running").ConfigureAwait(false);
            }

            List<Captain> analyzing = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Analyzing, token).ConfigureAwait(false);
            foreach (Captain captain in analyzing)
            {
                if (String.IsNullOrEmpty(captain.TenantId)) continue;
                bool released = await _Database.Captains.TryReleaseAsync(captain.TenantId!, captain.Id, CaptainStateEnum.Analyzing, token).ConfigureAwait(false);
                if (released) _Logging.Info(_Header + "returned captain " + captain.Id + " to Idle after an Admiral restart");
            }
        }

        /// <summary>
        /// Write the REPOSITORIES.md manifest for the given vessels into a directory.
        /// </summary>
        /// <param name="directory">Existing directory.</param>
        /// <param name="vessels">Vessels to describe.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Full path of the manifest.</returns>
        /// <exception cref="ArgumentNullException">Thrown when directory or vessels is null.</exception>
        public async Task<string> WriteManifestAsync(string directory, List<Vessel> vessels, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(directory)) throw new ArgumentNullException(nameof(directory));
            if (vessels == null) throw new ArgumentNullException(nameof(vessels));

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Repositories to categorize");
            sb.AppendLine();
            sb.AppendLine("Each section below describes one repository (an Armada vessel). Group every repository into exactly one fleet.");
            sb.AppendLine("Repository paths are on this machine; you may read them, but do not modify anything inside them.");
            sb.AppendLine();
            sb.AppendLine("Total repositories: " + vessels.Count);
            sb.AppendLine();

            foreach (Vessel vessel in vessels)
            {
                token.ThrowIfCancellationRequested();
                string? path = !String.IsNullOrWhiteSpace(vessel.WorkingDirectory) ? vessel.WorkingDirectory : vessel.LocalPath;
                bool exists = !String.IsNullOrWhiteSpace(path) && Directory.Exists(path);

                sb.AppendLine("## " + OneLine(vessel.Name));
                sb.AppendLine();
                sb.AppendLine("- Vessel ID: " + vessel.Id);
                sb.AppendLine("- Path: " + (String.IsNullOrWhiteSpace(path) ? "(no local path)" : path) + (exists || String.IsNullOrWhiteSpace(path) ? "" : " (not found on this machine)"));
                sb.AppendLine("- Remote URL: " + (String.IsNullOrWhiteSpace(vessel.RepoUrl) ? "(none)" : OneLine(vessel.RepoUrl!)));
                sb.AppendLine("- Default branch: " + OneLine(vessel.DefaultBranch));

                List<string> manifests = exists ? DetectManifests(path!) : new List<string>();
                List<string> languages = DetectLanguages(manifests);
                sb.AppendLine("- Detected languages: " + (languages.Count > 0 ? String.Join(", ", languages) : "(unknown)"));
                sb.AppendLine("- Top-level manifests: " + (manifests.Count > 0 ? String.Join(", ", manifests) : "(none found)"));
                sb.AppendLine();

                string? readme = exists ? ReadReadmeHead(path!) : null;
                if (!String.IsNullOrWhiteSpace(readme))
                {
                    sb.AppendLine("README (first " + _ReadmeLineLimit + " lines):");
                    sb.AppendLine();
                    sb.AppendLine("````text");
                    sb.AppendLine(readme);
                    sb.AppendLine("````");
                }
                else
                {
                    sb.AppendLine("README: (none found)");
                }

                sb.AppendLine();
            }

            string manifestPath = Path.Combine(directory, ManifestFileName);
            await File.WriteAllTextAsync(manifestPath, sb.ToString(), new UTF8Encoding(false), token).ConfigureAwait(false);
            return manifestPath;
        }

        /// <summary>
        /// Build the full prompt: the operator's instructions followed by the Admiral-owned output contract, which is
        /// always appended so edited instructions cannot break parsing.
        /// </summary>
        /// <param name="instructions">Operator instructions; null or empty uses a minimal default.</param>
        /// <param name="workingDirectory">Scratch directory the captain runs in.</param>
        /// <param name="vessels">Vessels the captain must categorize.</param>
        /// <returns>Prompt text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when workingDirectory or vessels is null.</exception>
        public string BuildPrompt(string? instructions, string workingDirectory, List<Vessel> vessels)
        {
            if (String.IsNullOrEmpty(workingDirectory)) throw new ArgumentNullException(nameof(workingDirectory));
            if (vessels == null) throw new ArgumentNullException(nameof(vessels));

            string outputPath = Path.Combine(workingDirectory, OutputFileName);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(String.IsNullOrWhiteSpace(instructions)
                ? "Look at all of the repositories listed in REPOSITORIES.md and group them into recommended fleets of related repositories."
                : instructions!.Trim());
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine("## Output contract (added by Armada; always follow it)");
            sb.AppendLine();
            sb.AppendLine("You are running as an Armada captain on a one-off analysis task. Nobody will answer questions; work autonomously and finish.");
            sb.AppendLine();
            sb.AppendLine("- Your working directory is: " + workingDirectory);
            sb.AppendLine("- The repositories are described in " + ManifestFileName + " in that directory (vessel ID, path, remote, languages, README excerpt).");
            sb.AppendLine("- You may read files inside the repositories at the absolute paths listed. Treat them as read-only: do not create, modify, or delete files there, and do not run commands that change them (no git commit, checkout, reset, or similar).");
            sb.AppendLine("- Write your answer to this file (create or overwrite it), and write nothing else anywhere: " + outputPath);
            sb.AppendLine("- The file must contain exactly one JSON object, with no Markdown fences or comments, in this shape:");
            sb.AppendLine();
            sb.AppendLine("{ \"fleets\": [ { \"name\": \"Fleet name\", \"description\": \"One sentence about what the fleet holds\", \"rationale\": \"Why these repositories belong together\", \"vesselIds\": [\"vsl_...\"] } ] }");
            sb.AppendLine();
            sb.AppendLine("- Every vessel ID below must appear in exactly one fleet's vesselIds. Use only these IDs:");
            foreach (Vessel vessel in vessels) sb.AppendLine("  - " + vessel.Id + " (" + OneLine(vessel.Name) + ")");
            sb.AppendLine();
            sb.AppendLine("When the file is written, reply with a one-line summary and stop.");
            return sb.ToString();
        }

        /// <summary>
        /// Parse and validate captain output into recommendations. Unknown vessel identifiers and duplicate
        /// assignments are dropped with a warning, fleets without valid vessels are dropped, fleets with the same name
        /// are merged, and vessels the captain left out are collected into an Uncategorized bucket.
        /// </summary>
        /// <param name="json">Contents of fleet-recommendations.json.</param>
        /// <param name="vessels">Vessels that were categorized.</param>
        /// <param name="warnings">Receives validation warnings.</param>
        /// <returns>Recommendations in display order, Uncategorized last.</returns>
        /// <exception cref="ArgumentNullException">Thrown when vessels or warnings is null.</exception>
        /// <exception cref="FleetRecommendationFormatException">Thrown when the JSON is empty, malformed, has no fleets, or references none of the vessels.</exception>
        public List<VesselImportFleetRecommendation> ParseRecommendations(string? json, List<Vessel> vessels, List<string> warnings)
        {
            if (vessels == null) throw new ArgumentNullException(nameof(vessels));
            if (warnings == null) throw new ArgumentNullException(nameof(warnings));
            if (String.IsNullOrWhiteSpace(json)) throw new FleetRecommendationFormatException(OutputFileName + " is empty.");

            string text = StripCodeFence(json!.Trim().TrimStart('\uFEFF'));
            FleetRecommendationDocument? document;
            try
            {
                document = JsonSerializer.Deserialize<FleetRecommendationDocument>(text, _ParseOptions);
            }
            catch (JsonException ex)
            {
                throw new FleetRecommendationFormatException(OutputFileName + " is not valid JSON: " + ex.Message, ex);
            }

            if (document == null || document.Fleets == null || document.Fleets.Count == 0)
                throw new FleetRecommendationFormatException(OutputFileName + " must contain a non-empty \"fleets\" array.");

            Dictionary<string, Vessel> known = vessels.GroupBy(v => v.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            HashSet<string> assigned = new HashSet<string>(StringComparer.Ordinal);
            List<VesselImportFleetRecommendation> results = new List<VesselImportFleetRecommendation>();
            Dictionary<string, VesselImportFleetRecommendation> byName = new Dictionary<string, VesselImportFleetRecommendation>(StringComparer.OrdinalIgnoreCase);

            int index = 0;
            foreach (FleetRecommendationDocumentFleet? entry in document.Fleets)
            {
                index++;
                if (entry == null) continue;

                string name = (entry.Name ?? String.Empty).Trim();
                if (name.Length == 0)
                {
                    name = "Fleet " + index;
                    warnings.Add("Fleet #" + index + " had no name; named it \"" + name + "\".");
                }

                List<string> ids = new List<string>();
                foreach (string? raw in entry.VesselIds ?? new List<string>())
                {
                    string id = (raw ?? String.Empty).Trim();
                    if (id.Length == 0) continue;
                    if (!known.ContainsKey(id))
                    {
                        warnings.Add("Fleet \"" + name + "\" referenced unknown vessel " + Truncate(id, 80) + "; it was dropped.");
                        continue;
                    }

                    if (!assigned.Add(id))
                    {
                        warnings.Add("Vessel " + id + " was assigned to more than one fleet; kept its first fleet.");
                        continue;
                    }

                    ids.Add(id);
                }

                if (ids.Count == 0)
                {
                    warnings.Add("Fleet \"" + name + "\" had no valid vessels and was dropped.");
                    continue;
                }

                if (String.Equals(name, UncategorizedFleetName, StringComparison.OrdinalIgnoreCase)) name = UncategorizedFleetName;

                if (byName.TryGetValue(name, out VesselImportFleetRecommendation? existing))
                {
                    warnings.Add("Fleet name \"" + name + "\" appeared more than once; the fleets were merged.");
                    existing.VesselIds.AddRange(ids);
                    continue;
                }

                if (results.Count >= _MaxFleets)
                {
                    foreach (string id in ids) assigned.Remove(id);
                    warnings.Add("More than " + _MaxFleets + " fleets were recommended; \"" + name + "\" was dropped.");
                    continue;
                }

                VesselImportFleetRecommendation recommendation = new VesselImportFleetRecommendation();
                recommendation.Name = name;
                recommendation.Description = String.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description;
                recommendation.Rationale = String.IsNullOrWhiteSpace(entry.Rationale) ? null : entry.Rationale;
                recommendation.VesselIds = ids;
                results.Add(recommendation);
                byName[name] = recommendation;
            }

            if (assigned.Count == 0)
                throw new FleetRecommendationFormatException(OutputFileName + " did not assign any of the " + vessels.Count + " imported vessels to a fleet.");

            List<string> leftOver = vessels.Select(v => v.Id).Distinct(StringComparer.Ordinal).Where(id => !assigned.Contains(id)).ToList();
            if (leftOver.Count > 0)
            {
                warnings.Add(leftOver.Count + " vessel(s) were not assigned by the captain and were placed in " + UncategorizedFleetName + ".");
                if (byName.TryGetValue(UncategorizedFleetName, out VesselImportFleetRecommendation? bucket))
                {
                    bucket.VesselIds.AddRange(leftOver);
                }
                else
                {
                    VesselImportFleetRecommendation uncategorized = new VesselImportFleetRecommendation();
                    uncategorized.Name = UncategorizedFleetName;
                    uncategorized.Description = "Repositories the captain did not assign to a fleet.";
                    uncategorized.VesselIds = leftOver;
                    results.Add(uncategorized);
                }
            }

            VesselImportFleetRecommendation? last = results.FirstOrDefault(r => r.Name == UncategorizedFleetName);
            if (last != null)
            {
                results.Remove(last);
                results.Add(last);
            }

            for (int i = 0; i < results.Count; i++) results[i].SortOrder = i;
            return results;
        }

        #endregion

        #region Private-Methods

        private static bool IsCategorizationActive(VesselImportBatch batch)
        {
            return batch.CategorizationStatus == VesselImportCategorizationStatusEnum.Pending
                || batch.CategorizationStatus == VesselImportCategorizationStatusEnum.Running;
        }

        private void ReleaseBatch(string batchId)
        {
            lock (_ActiveLock)
            {
                _ActiveBatches.Remove(batchId);
            }
        }

        /// <summary>
        /// Vessels the operator selected in the batch: created ones and ones that already existed, that still exist in
        /// the tenant.
        /// </summary>
        private async Task<List<Vessel>> ResolveBatchVesselsAsync(string tenantId, string batchId, CancellationToken token)
        {
            List<VesselImportItem> items = await _Database.VesselImportItems.EnumerateByBatchAsync(tenantId, batchId, token).ConfigureAwait(false);
            List<string> ids = new List<string>();
            foreach (VesselImportItem item in items)
            {
                if (!item.Selected) continue;
                if (item.Outcome == VesselImportOutcomeEnum.Created && !String.IsNullOrEmpty(item.VesselId)) ids.Add(item.VesselId!);
                else if (item.Outcome == VesselImportOutcomeEnum.SkippedExisting && !String.IsNullOrEmpty(item.ExistingVesselId)) ids.Add(item.ExistingVesselId!);
            }

            List<Vessel> vessels = new List<Vessel>();
            foreach (string id in ids.Distinct(StringComparer.Ordinal))
            {
                Vessel? vessel = await _Database.Vessels.ReadAsync(tenantId, id, token).ConfigureAwait(false);
                if (vessel != null) vessels.Add(vessel);
            }

            return vessels;
        }

        private TimeSpan ResolveTimeout()
        {
            return _TimeoutOverride ?? TimeSpan.FromMinutes(_Settings.Import.CategorizationTimeoutMinutes);
        }

        private async Task RunJobAsync(Job job, VesselImportBatch batch, List<Vessel> vessels, string? userId)
        {
            string tenantId = batch.TenantId!;
            string captainId = batch.CategorizationCaptainId ?? String.Empty;
            string workingDirectory = Path.Combine(_Settings.DataDirectory, "fleet-categorization", job.Id);
            bool reserved = false;
            bool succeeded = false;
            string? resultJson = null;
            string? failureMessage = null;
            bool cancelled = false;

            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                Task? monitor = null;
                try
                {
                    // Conditional on Queued: a job cancelled before this worker started stays Cancelled and the
                    // captain is never run.
                    if (!await _Jobs.TryStartAsync(job, 5).ConfigureAwait(false))
                    {
                        _Logging.Info(_Header + "job " + job.Id + " was cancelled before it started; not running the captain");
                        cancelled = true;
                        return;
                    }

                    batch.CategorizationStatus = VesselImportCategorizationStatusEnum.Running;
                    batch.CategorizationStartedUtc = DateTime.UtcNow;
                    await _Database.VesselImportBatches.UpdateAsync(batch).ConfigureAwait(false);

                    Captain? captain = String.IsNullOrEmpty(captainId) ? null : await _Database.Captains.ReadAsync(tenantId, captainId).ConfigureAwait(false);
                    if (captain == null) throw new KeyNotFoundException("Captain not found: " + (String.IsNullOrEmpty(captainId) ? "(none)" : captainId) + ".");

                    reserved = await _Database.Captains.TryReserveAsync(tenantId, captain.Id, CaptainStateEnum.Analyzing).ConfigureAwait(false);
                    if (!reserved)
                    {
                        Captain? current = await _Database.Captains.ReadAsync(tenantId, captain.Id).ConfigureAwait(false);
                        throw new InvalidOperationException("Captain " + captain.Name + " is not idle (state " + (current?.State.ToString() ?? "unknown")
                            + "). Choose an idle captain or retry when it is free.");
                    }

                    Directory.CreateDirectory(workingDirectory);
                    await WriteManifestAsync(workingDirectory, vessels, cts.Token).ConfigureAwait(false);
                    string prompt = BuildPrompt(batch.CategorizationPrompt, workingDirectory, vessels);
                    await File.WriteAllTextAsync(Path.Combine(workingDirectory, "PROMPT.md"), prompt, new UTF8Encoding(false)).ConfigureAwait(false);

                    monitor = MonitorJobAsync(job.Id, cts);
                    TimeSpan timeout = ResolveTimeout();
                    _Logging.Info(_Header + "captain " + captain.Id + " categorizing " + vessels.Count + " vessels for batch " + batch.Id + " in " + workingDirectory);

                    CaptainPromptResult run = await _Runner.RunAsync(
                        captain,
                        workingDirectory,
                        prompt,
                        timeout,
                        Path.Combine(workingDirectory, "captain.log"),
                        async (int processId) => await RecordProcessAsync(tenantId, captain.Id, processId).ConfigureAwait(false),
                        cts.Token).ConfigureAwait(false);

                    // The captain's part is over once its process has returned: give it back before anything else is
                    // recorded, so no observer can see a finished batch or job while the captain is still Analyzing.
                    reserved = !await ReleaseCaptainAsync(tenantId, captainId).ConfigureAwait(false);

                    if (run.Cancelled || cts.IsCancellationRequested)
                    {
                        cancelled = true;
                        return;
                    }

                    if (!String.IsNullOrEmpty(run.Error)) throw new InvalidOperationException("The captain could not be started: " + run.Error);
                    if (run.TimedOut)
                        throw new TimeoutException("The captain did not finish within " + FormatMinutes(timeout) + ". Raise Import.CategorizationTimeoutMinutes or retry with fewer repositories.");

                    string outputPath = Path.Combine(workingDirectory, OutputFileName);
                    List<string> warnings = new List<string>();
                    string? json = null;
                    if (File.Exists(outputPath))
                    {
                        json = await File.ReadAllTextAsync(outputPath).ConfigureAwait(false);
                    }
                    else
                    {
                        json = ExtractRecommendationJson(run.FinalMessage) ?? ExtractRecommendationJson(run.Output);
                        if (json == null)
                        {
                            throw new FleetRecommendationFormatException("The captain exited" + (run.ExitCode.HasValue ? " with code " + run.ExitCode.Value : "")
                                + " without writing " + OutputFileName + "." + DescribeOutput(run.Output));
                        }

                        warnings.Add("The captain did not write " + OutputFileName + "; its reply contained the recommendations, which were used instead.");
                    }

                    List<VesselImportFleetRecommendation> recommendations = ParseRecommendations(json, vessels, warnings);
                    foreach (string warning in warnings) _Logging.Warn(_Header + "batch " + batch.Id + ": " + warning);

                    recommendations = await _Database.VesselImportFleetRecommendations.ReplaceForBatchAsync(tenantId, batch.Id, recommendations).ConfigureAwait(false);

                    batch.CategorizationStatus = VesselImportCategorizationStatusEnum.Completed;
                    batch.CategorizationError = null;
                    batch.CategorizationCompletedUtc = DateTime.UtcNow;
                    await _Database.VesselImportBatches.UpdateAsync(batch).ConfigureAwait(false);

                    bool applied = false;
                    if (batch.CategorizationApplyAutomatically)
                    {
                        FleetRecommendationApplyRequest apply = new FleetRecommendationApplyRequest();
                        foreach (VesselImportFleetRecommendation recommendation in recommendations)
                        {
                            FleetRecommendationApplyFleet fleet = new FleetRecommendationApplyFleet();
                            fleet.Name = recommendation.Name;
                            fleet.Description = recommendation.Description;
                            fleet.VesselIds = new List<string>(recommendation.VesselIds);
                            apply.Fleets.Add(fleet);
                        }

                        await ApplyInternalAsync(batch, vessels, recommendations, apply, userId, CancellationToken.None).ConfigureAwait(false);
                        applied = true;
                    }

                    FleetCategorizationJobSummary summary = new FleetCategorizationJobSummary();
                    summary.BatchId = batch.Id;
                    summary.FleetCount = recommendations.Count;
                    summary.VesselCount = vessels.Count;
                    summary.UncategorizedCount = recommendations.Where(r => r.Name == UncategorizedFleetName).Sum(r => r.VesselIds.Count);
                    summary.Applied = applied;
                    summary.Warnings = warnings;
                    resultJson = JsonSerializer.Serialize(summary, _ResultJsonOptions);

                    succeeded = true;
                    _Logging.Info(_Header + "batch " + batch.Id + " categorized into " + recommendations.Count + " fleets" + (applied ? " (applied)" : ""));
                }
                catch (Exception ex)
                {
                    failureMessage = cts.IsCancellationRequested && ex is OperationCanceledException ? "Fleet categorization was cancelled." : ex.Message;
                    _Logging.Warn(_Header + "fleet categorization of batch " + batch.Id + " failed: " + failureMessage);
                }
                finally
                {
                    // Finish in a fixed order. The job's terminal state is the completion signal callers wait for, so it
                    // is written last, after the captain is Idle, the batch is in its final state, and the batch can be
                    // categorized again. Heartbeats and the terminal write are conditional on the stored status, so a
                    // cancel is never overwritten and the first terminal status wins; a cancelled job gets no further
                    // job write here.
                    cts.Cancel();
                    if (monitor != null)
                    {
                        try { await monitor.ConfigureAwait(false); }
                        catch (Exception) { }
                    }

                    if (reserved) await ReleaseCaptainAsync(tenantId, captainId).ConfigureAwait(false);

                    if (cancelled) await FailBatchAsync(batch, "Fleet categorization was cancelled.").ConfigureAwait(false);
                    else if (failureMessage != null) await FailBatchAsync(batch, failureMessage).ConfigureAwait(false);

                    if (succeeded && !KeepWorkingDirectory) TryDeleteDirectory(workingDirectory);
                    ReleaseBatch(batch.Id);

                    if (resultJson != null) await CompleteJobAsync(job.Id, resultJson).ConfigureAwait(false);
                    else if (failureMessage != null) await FailJobAsync(job.Id, failureMessage).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Return a captain reserved for categorization to Idle.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="captainId">Captain identifier.</param>
        /// <returns>True when the release was attempted without an error (the captain is no longer held by this run).</returns>
        private async Task<bool> ReleaseCaptainAsync(string tenantId, string captainId)
        {
            try
            {
                await _Database.Captains.TryReleaseAsync(tenantId, captainId, CaptainStateEnum.Analyzing).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not return captain " + captainId + " to Idle: " + ex.Message);
                return false;
            }
        }

        private async Task MonitorJobAsync(string jobId, CancellationTokenSource cts)
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_JobPollIntervalMs, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    // The heartbeat only touches the update time and progress of a Running job, so it cannot undo a
                    // cancel; when it reports the job is no longer Running, the job was cancelled (or failed by the
                    // stale-job reaper) and the captain is stopped.
                    if (!await _Jobs.HeartbeatAsync(jobId, 50).ConfigureAwait(false))
                    {
                        _Logging.Info(_Header + "job " + jobId + " is no longer running (cancelled); stopping the captain");
                        cts.Cancel();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "heartbeat for job " + jobId + " failed: " + ex.Message);
                }
            }
        }

        private async Task RecordProcessAsync(string tenantId, string captainId, int processId)
        {
            try
            {
                Captain? captain = await _Database.Captains.ReadAsync(tenantId, captainId).ConfigureAwait(false);
                if (captain == null || captain.State != CaptainStateEnum.Analyzing) return;
                captain.ProcessId = processId;
                await _Database.Captains.UpdateAsync(captain).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not record process " + processId + " for captain " + captainId + ": " + ex.Message);
            }
        }

        private async Task<FleetRecommendationApplyResult> ApplyInternalAsync(
            VesselImportBatch batch,
            List<Vessel> batchVessels,
            List<VesselImportFleetRecommendation> previous,
            FleetRecommendationApplyRequest request,
            string? userId,
            CancellationToken token)
        {
            string tenantId = batch.TenantId!;
            Dictionary<string, Vessel> allowed = batchVessels.GroupBy(v => v.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            List<FleetRecommendationApplyFleet> entries = request.Fleets.Where(f => f != null && f.VesselIds.Any(v => !String.IsNullOrWhiteSpace(v))).ToList();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<string> unknown = new List<string>();
            foreach (FleetRecommendationApplyFleet entry in entries)
            {
                if (String.IsNullOrWhiteSpace(entry.Name)) throw new ArgumentException("Every fleet with vessels needs a name.", nameof(request));
                if (entry.Name.Trim().Length > 256) throw new ArgumentException("Fleet name is too long (maximum 256 characters): " + entry.Name.Trim().Substring(0, 40) + "...", nameof(request));
                foreach (string raw in entry.VesselIds)
                {
                    if (String.IsNullOrWhiteSpace(raw)) continue;
                    string id = raw.Trim();
                    if (!allowed.ContainsKey(id)) unknown.Add(id);
                    else if (!seen.Add(id)) throw new ArgumentException("Vessel " + id + " is listed in more than one fleet.", nameof(request));
                }
            }

            if (unknown.Count > 0)
            {
                throw new ArgumentException(unknown.Count + " vessel(s) are not part of import batch " + batch.Id + ": "
                    + String.Join(", ", unknown.Take(5)) + (unknown.Count > 5 ? ", ..." : ""), nameof(request));
            }

            List<Fleet> tenantFleets = await _Database.Fleets.EnumerateAsync(tenantId, token).ConfigureAwait(false);
            Dictionary<string, Fleet> fleetsByName = new Dictionary<string, Fleet>(StringComparer.OrdinalIgnoreCase);
            foreach (Fleet fleet in tenantFleets.OrderBy(f => f.CreatedUtc))
            {
                string key = fleet.Name.Trim();
                if (!fleetsByName.ContainsKey(key)) fleetsByName[key] = fleet;
            }

            Dictionary<string, VesselImportFleetRecommendation> previousByName = new Dictionary<string, VesselImportFleetRecommendation>(StringComparer.OrdinalIgnoreCase);
            foreach (VesselImportFleetRecommendation rec in previous)
            {
                if (!previousByName.ContainsKey(rec.Name)) previousByName[rec.Name] = rec;
            }

            FleetRecommendationApplyResult result = new FleetRecommendationApplyResult();
            result.BatchId = batch.Id;
            List<VesselImportFleetRecommendation> stored = new List<VesselImportFleetRecommendation>();
            int order = 0;

            foreach (FleetRecommendationApplyFleet entry in entries)
            {
                token.ThrowIfCancellationRequested();
                string name = entry.Name.Trim();
                List<string> ids = entry.VesselIds.Where(v => !String.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.Ordinal).ToList();

                VesselImportFleetRecommendation record = new VesselImportFleetRecommendation();
                record.Name = name;
                record.Description = String.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description;
                record.Rationale = previousByName.TryGetValue(name, out VesselImportFleetRecommendation? original) ? original.Rationale : null;
                record.SortOrder = order++;
                record.VesselIds = ids;
                stored.Add(record);

                if (String.Equals(name, UncategorizedFleetName, StringComparison.OrdinalIgnoreCase)) continue;

                if (!fleetsByName.TryGetValue(name, out Fleet? target))
                {
                    target = new Fleet(name);
                    target.TenantId = tenantId;
                    target.UserId = userId;
                    target.Description = record.Description;
                    target = await _Database.Fleets.CreateAsync(target, token).ConfigureAwait(false);
                    fleetsByName[name] = target;
                    result.CreatedFleetIds.Add(target.Id);
                }

                if (!result.Fleets.Any(f => f.Id == target.Id)) result.Fleets.Add(target);
                record.AppliedFleetId = target.Id;

                foreach (string id in ids)
                {
                    Vessel? vessel = await _Database.Vessels.ReadAsync(tenantId, id, token).ConfigureAwait(false);
                    if (vessel == null) continue;

                    FleetRecommendationAssignment assignment = new FleetRecommendationAssignment();
                    assignment.VesselId = vessel.Id;
                    assignment.VesselName = vessel.Name;
                    assignment.PreviousFleetId = vessel.FleetId;
                    assignment.FleetId = target.Id;
                    assignment.FleetName = target.Name;

                    if (!String.Equals(vessel.FleetId, target.Id, StringComparison.Ordinal))
                    {
                        vessel.FleetId = target.Id;
                        await _Database.Vessels.UpdateAsync(vessel, token).ConfigureAwait(false);
                    }

                    result.Assignments.Add(assignment);
                }
            }

            await _Database.VesselImportFleetRecommendations.ReplaceForBatchAsync(tenantId, batch.Id, stored, token).ConfigureAwait(false);

            batch.CategorizationStatus = VesselImportCategorizationStatusEnum.Applied;
            batch.CategorizationError = null;
            batch.CategorizationCompletedUtc = DateTime.UtcNow;
            VesselImportBatch updated = await _Database.VesselImportBatches.UpdateAsync(batch, token).ConfigureAwait(false);
            batch.LastUpdateUtc = updated.LastUpdateUtc;
            result.Batch = batch;

            _Logging.Info(_Header + "applied fleet recommendations for batch " + batch.Id + ": " + result.Assignments.Count + " vessels into "
                + result.Fleets.Count + " fleets (" + result.CreatedFleetIds.Count + " created)");
            return result;
        }

        private async Task FailBatchAsync(VesselImportBatch batch, string message)
        {
            try
            {
                batch.CategorizationStatus = VesselImportCategorizationStatusEnum.Failed;
                batch.CategorizationError = Truncate(message, 4000);
                batch.CategorizationCompletedUtc = DateTime.UtcNow;
                await _Database.VesselImportBatches.UpdateAsync(batch).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not mark categorization of batch " + batch.Id + " failed: " + ex.Message);
            }
        }

        private async Task CompleteJobAsync(string jobId, string resultJson)
        {
            try
            {
                await _Jobs.TryFinishAsync(jobId, JobStatusEnum.Succeeded, resultJson, null).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not mark job " + jobId + " succeeded: " + ex.Message);
            }
        }

        private async Task FailJobAsync(string jobId, string message)
        {
            try
            {
                await _Jobs.TryFinishAsync(jobId, JobStatusEnum.Failed, null, Truncate(message, 4000)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not mark job " + jobId + " failed: " + ex.Message);
            }
        }

        private static List<string> DetectManifests(string path)
        {
            List<string> found = new List<string>();
            try
            {
                foreach (string file in Directory.EnumerateFiles(path).Select(Path.GetFileName).Where(n => !String.IsNullOrEmpty(n)).Select(n => n!).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                {
                    if (_ManifestLanguages.ContainsKey(file) || _ManifestExtensions.ContainsKey(Path.GetExtension(file))) found.Add(file);
                    if (found.Count >= 25) break;
                }
            }
            catch (Exception)
            {
                // Unreadable directories simply report no manifests.
            }

            return found;
        }

        private static List<string> DetectLanguages(List<string> manifests)
        {
            List<string> languages = new List<string>();
            foreach (string manifest in manifests)
            {
                string? language = null;
                if (_ManifestLanguages.TryGetValue(manifest, out string? byName)) language = byName;
                else if (_ManifestExtensions.TryGetValue(Path.GetExtension(manifest), out string? byExtension)) language = byExtension;
                if (language != null && !languages.Contains(language)) languages.Add(language);
            }

            return languages;
        }

        private string? ReadReadmeHead(string path)
        {
            if (_ReadmeLineLimit == 0) return null;
            try
            {
                string? readme = null;
                foreach (string name in _ReadmeNames)
                {
                    string candidate = Path.Combine(path, name);
                    if (File.Exists(candidate))
                    {
                        readme = candidate;
                        break;
                    }
                }

                if (readme == null)
                {
                    readme = Directory.EnumerateFiles(path)
                        .FirstOrDefault(f => Path.GetFileName(f).StartsWith("readme", StringComparison.OrdinalIgnoreCase));
                }

                if (readme == null) return null;

                List<string> lines = new List<string>();
                using (StreamReader reader = new StreamReader(readme))
                {
                    string? line;
                    while (lines.Count < _ReadmeLineLimit && (line = reader.ReadLine()) != null)
                    {
                        lines.Add(Truncate(line.Replace("````", "'''"), 400));
                    }
                }

                return String.Join("\n", lines).TrimEnd();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Find a JSON object with a non-empty "fleets" list in free text (a captain reply): a fenced json block, or a
        /// balanced object found by the shared string-aware scanner, deserialized into the typed recommendation document.
        /// Returns null when none is found.
        /// </summary>
        private static string? ExtractRecommendationJson(string? text)
        {
            if (EmbeddedJsonExtractor.TryExtract<FleetRecommendationDocument>(
                    text,
                    doc => doc.Fleets != null && doc.Fleets.Count > 0,
                    out FleetRecommendationDocument? _,
                    out string? json))
                return json;

            return null;
        }

        private static string StripCodeFence(string text)
        {
            if (!text.StartsWith("```", StringComparison.Ordinal)) return text;
            int firstNewline = text.IndexOf('\n');
            int lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline < 0 || lastFence <= firstNewline) return text;
            return text.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
        }

        private static string DescribeOutput(string output)
        {
            if (String.IsNullOrWhiteSpace(output)) return "";
            string tail = output.Trim();
            if (tail.Length > 500) tail = "..." + tail.Substring(tail.Length - 500);
            return " Last output: " + tail;
        }

        private static string FormatMinutes(TimeSpan timeout)
        {
            if (timeout.TotalMinutes >= 1) return Math.Round(timeout.TotalMinutes, 1) + " minute(s)";
            return Math.Round(timeout.TotalSeconds) + " second(s)";
        }

        private static string OneLine(string value)
        {
            return (value ?? String.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private static string Truncate(string value, int max)
        {
            if (value == null) return String.Empty;
            return value.Length > max ? value.Substring(0, max) : value;
        }

        private void TryDeleteDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch (Exception ex)
            {
                _Logging.Debug(_Header + "could not delete " + directory + ": " + ex.Message);
            }
        }

        #endregion
    }
}
