namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Finds or creates the model profile of what a captain runs and its current revision (docs/MODEL_HETEROGENEITY.md,
    /// Phase 5 item 1), and points the captain at the profile. Profiles are advisory: nothing here sets a captain's tier
    /// or changes routing or dispatch. Callers that learn a captain's runtime and model in passing (stage outcomes, Quick
    /// probe runs) use <see cref="TryResolveAsync"/>, which never throws.
    /// </summary>
    public class ModelProfileService
    {
        #region Private-Members

        private readonly string _Header = "[ModelProfileService] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;

        // Find-or-create is serialized in the process so two observations of a new model make one profile and one
        // revision; the unique identity key is the net for anything else.
        private static readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ModelProfileService(DatabaseDriver database, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Find or create the profile for an observed identity and return its current revision, creating a new revision
        /// when <see cref="ModelProfileRules.StartsNewRevision"/> says so (otherwise the observation pools into the
        /// current one). When the observation names a captain, the captain is pointed at the profile. Idempotent: the
        /// same observation twice gives the same profile and revision.
        /// </summary>
        /// <param name="observation">Observation.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The profile, the current revision, and whether either was created.</returns>
        /// <exception cref="ArgumentNullException">Thrown when observation is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the observation has no runtime.</exception>
        public async Task<ModelProfileResolution> ResolveAsync(ModelProfileObservation observation, CancellationToken token = default)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            ModelProfileIdentity identity = ModelProfileRules.IdentityOf(observation);
            if (String.IsNullOrEmpty(identity.Runtime)) throw new ArgumentException("The observation has no runtime.", nameof(observation));
            string? tenantId = String.IsNullOrWhiteSpace(observation.TenantId) ? null : observation.TenantId.Trim();
            string key = ModelProfileRules.IdentityKey(tenantId, identity);

            ModelProfileResolution resolution = new ModelProfileResolution();
            await _Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ModelProfile? profile = await _Database.ModelProfiles.ReadByIdentityKeyAsync(key, token).ConfigureAwait(false);
                if (profile == null)
                {
                    ModelProfile created = new ModelProfile { TenantId = tenantId, Identity = identity };
                    try
                    {
                        profile = await _Database.ModelProfiles.CreateAsync(created, key, token).ConfigureAwait(false);
                        resolution.ProfileCreated = true;
                    }
                    catch (DuplicateEntityException)
                    {
                        // Another process created it first.
                        profile = await _Database.ModelProfiles.ReadByIdentityKeyAsync(key, token).ConfigureAwait(false);
                        if (profile == null) throw;
                    }
                }

                List<ModelProfileRevision> revisions = await _Database.ModelProfiles.ReadRevisionsAsync(profile.Id, token).ConfigureAwait(false);
                ModelProfileRevision? current = revisions.Count > 0 ? revisions[revisions.Count - 1] : null;
                if (ModelProfileRules.StartsNewRevision(current, observation))
                {
                    current = await _Database.ModelProfiles.CreateRevisionAsync(ModelProfileRules.RevisionFor(profile, observation), token).ConfigureAwait(false);
                    resolution.RevisionCreated = true;
                    _Logging.Info(_Header + "profile " + profile.Id + " (" + Describe(profile.Identity) + ") has a new revision " + current.Id
                        + (resolution.ProfileCreated ? " (new profile)" : String.Empty));
                }

                resolution.Profile = profile;
                resolution.Revision = current!;
            }
            finally
            {
                _Gate.Release();
            }

            if (!String.IsNullOrWhiteSpace(observation.CaptainId))
                await _Database.ModelProfiles.LinkCaptainAsync(observation.CaptainId!.Trim(), resolution.Profile.Id, token).ConfigureAwait(false);
            return resolution;
        }

        /// <summary>
        /// Record a Full Evaluate report as an advisory profile update: a new immutable revision that repeats the
        /// provenance of the revision the run was recorded on, carries the report, and is marked compatible with that
        /// revision so the scorecard pools the two. Nothing reads it for routing, persona captain lists or prompts.
        /// </summary>
        /// <param name="resolution">The profile and revision the run was recorded on.</param>
        /// <param name="report">The report.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The new revision.</returns>
        /// <exception cref="ArgumentNullException">Thrown when resolution or report is null.</exception>
        public async Task<ModelProfileRevision> RecordEvaluationAsync(ModelProfileResolution resolution, ModelEvaluationReport report, CancellationToken token = default)
        {
            if (resolution == null) throw new ArgumentNullException(nameof(resolution));
            if (report == null) throw new ArgumentNullException(nameof(report));
            ModelProfileRevision basis = resolution.Revision;
            report.ProfileId = resolution.Profile.Id;
            ModelProfileRevision advisory = new ModelProfileRevision
            {
                ProfileId = resolution.Profile.Id,
                TenantId = resolution.Profile.TenantId,
                RuntimeVersion = basis.RuntimeVersion,
                ModelDigest = basis.ModelDigest,
                Sampling = new ModelProfileSampling { Temperature = basis.Sampling.Temperature, TopP = basis.Sampling.TopP },
                ToolConfiguration = new ModelProfileToolConfiguration { Tools = basis.ToolConfiguration.Tools, Mcp = basis.ToolConfiguration.Mcp },
                FirstSeenUtc = DateTime.UtcNow,
                EvaluationRunId = report.RunId
            };
            report.ProfileRevisionId = advisory.Id;
            advisory.EvaluationReport = report;

            await _Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                advisory = await _Database.ModelProfiles.CreateRevisionAsync(advisory, token).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(basis.Id))
                {
                    List<string> compatible = new List<string>(basis.CompatibleWith);
                    if (!compatible.Contains(basis.Id)) compatible.Add(basis.Id);
                    await _Database.ModelProfiles.SetCompatibleWithAsync(advisory.Id, compatible, token).ConfigureAwait(false);
                    advisory.CompatibleWith = compatible;
                }
            }
            finally
            {
                _Gate.Release();
            }

            _Logging.Info(_Header + "profile " + resolution.Profile.Id + " has advisory revision " + advisory.Id + " from Full Evaluate " + report.RunId);
            return advisory;
        }

        /// <summary>
        /// <see cref="ResolveAsync"/> for callers that only observe in passing: any failure is logged and gives null, so
        /// it never fails a mission or a probe run. An observation without a runtime gives null.
        /// </summary>
        /// <param name="observation">Observation, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The resolution, or null.</returns>
        public async Task<ModelProfileResolution?> TryResolveAsync(ModelProfileObservation? observation, CancellationToken token = default)
        {
            if (observation == null || String.IsNullOrWhiteSpace(observation.Runtime)) return null;
            try
            {
                return await ResolveAsync(observation, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not record the model profile for captain " + (observation.CaptainId ?? "(none)") + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// The observation a stage outcome's provenance describes (runtime, provider, model, configured context as the
        /// served context, runtime version, model digest, sampling and tool settings), linked to its captain.
        /// </summary>
        /// <param name="outcome">Stage outcome.</param>
        /// <returns>The observation, or null when the outcome has no runtime.</returns>
        public static ModelProfileObservation? ObservationFrom(StageOutcome? outcome)
        {
            if (outcome == null || !outcome.Runtime.HasValue) return null;
            return new ModelProfileObservation
            {
                TenantId = outcome.TenantId,
                CaptainId = outcome.CaptainId,
                Runtime = outcome.Runtime.Value.ToString(),
                Provider = outcome.ModelProvider,
                Model = outcome.Model,
                ServedContextTokens = outcome.ContextTokens,
                RuntimeVersion = outcome.RuntimeVersion,
                ModelDigest = outcome.ModelDigest,
                Temperature = outcome.Temperature,
                TopP = outcome.TopP,
                ToolsEnabled = outcome.ToolsEnabled,
                McpToolsEnabled = outcome.McpToolsEnabled
            };
        }

        /// <summary>
        /// The observation a Quick probe run describes: its runtime, provider, model, measured served context (else the
        /// configured one), runtime version and model digest, with the endpoint's sampling and tool settings when it ran
        /// through one. A captain run is linked to its captain; a model endpoint run is not linked to any captain.
        /// </summary>
        /// <param name="run">Probe run.</param>
        /// <param name="endpoint">Model endpoint the run used, or null.</param>
        /// <returns>The observation, or null when the run has no runtime.</returns>
        public static ModelProfileObservation? ObservationFrom(ModelProbeRun? run, ModelEndpoint? endpoint)
        {
            if (run == null || String.IsNullOrWhiteSpace(run.Runtime)) return null;
            ModelProfileObservation observation = new ModelProfileObservation
            {
                TenantId = run.TenantId,
                CaptainId = run.TargetKind == Enums.ModelProbeTargetEnum.Captain ? run.CaptainId : null,
                Runtime = run.Runtime!,
                Provider = run.ModelProvider,
                Model = run.Model,
                ServedContextTokens = run.ServedContextTokens ?? run.ConfiguredContextTokens,
                RuntimeVersion = run.RuntimeVersion,
                ModelDigest = run.ModelDigest
            };
            if (endpoint != null && endpoint.InferenceOptions != null)
            {
                observation.Temperature = endpoint.InferenceOptions.Temperature;
                observation.TopP = endpoint.InferenceOptions.TopP;
                observation.ToolsEnabled = endpoint.InferenceOptions.ToolsEnabled;
                observation.McpToolsEnabled = endpoint.InferenceOptions.McpToolsEnabled;
            }

            return observation;
        }

        #endregion

        #region Private-Methods

        private static string Describe(ModelProfileIdentity identity)
        {
            return identity.Runtime + "/" + (identity.Provider ?? "-") + "/" + (identity.Model ?? "-") + "/"
                + (identity.ServedContextTokens.HasValue ? identity.ServedContextTokens.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-");
        }

        #endregion
    }
}
