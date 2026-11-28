namespace Armada.DemoSeed
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Writes two weeks of stage outcomes (so the Reliability summary has numbers per model and persona) and a model
    /// profile per distinct captain model, with a tier and a measured scorecard from a completed evaluation.
    /// </summary>
    public class DemoHistorySeeder
    {
        #region Private-Members

        private readonly DatabaseDriver _Db;
        private readonly string _Tenant;
        private readonly string _User;
        private readonly DateTime _Now;
        private readonly Dictionary<string, ModelProfile> _Profiles = new Dictionary<string, ModelProfile>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="db">Initialized database driver.</param>
        /// <param name="tenant">Tenant id.</param>
        /// <param name="user">User id.</param>
        /// <param name="now">Reference time.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="db"/> is null.</exception>
        public DemoHistorySeeder(DatabaseDriver db, string tenant, string user, DateTime now)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Tenant = tenant;
            _User = user;
            _Now = now;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record one stage outcome for a mission.
        /// </summary>
        /// <param name="db">Database driver.</param>
        /// <param name="mission">Mission.</param>
        /// <param name="captain">Captain that ran the attempt.</param>
        /// <param name="attempt">Attempt number, from 1.</param>
        /// <param name="disposition">How the attempt ended.</param>
        /// <param name="failureKind">Failure kind, or null.</param>
        /// <param name="repair">Contract repair kind, or null.</param>
        /// <param name="startedUtc">Start.</param>
        /// <param name="endedUtc">End.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static async Task AddOutcomeAsync(DatabaseDriver db, Mission mission, Captain captain, int attempt, StageDispositionEnum disposition,
            MissionFailureKindEnum? failureKind, ContractRepairKindEnum? repair, DateTime startedUtc, DateTime endedUtc)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (captain == null) throw new ArgumentNullException(nameof(captain));

            StageOutcome outcome = new StageOutcome();
            outcome.TenantId = mission.TenantId;
            outcome.UserId = mission.UserId;
            outcome.MissionId = mission.Id;
            outcome.VoyageId = mission.VoyageId;
            outcome.VesselId = mission.VesselId;
            outcome.CaptainId = captain.Id;
            outcome.CaptainName = captain.Name;
            outcome.Persona = mission.Persona;
            outcome.Attempt = attempt;
            outcome.Runtime = captain.Runtime;
            outcome.Model = captain.Model;
            outcome.ModelProvider = ProviderOf(captain);
            outcome.ModelEndpointId = captain.ModelEndpointId;
            outcome.Disposition = disposition;
            outcome.FailureKind = failureKind;
            outcome.RuntimeFailureKind = failureKind.HasValue ? RuntimeFailureKindEnum.TaskFailure : RuntimeFailureKindEnum.Clean;
            outcome.ContractRepairKind = repair;
            outcome.JudgeVerdict = String.Equals(mission.Persona, PersonaCatalog.Judge, StringComparison.Ordinal) && disposition == StageDispositionEnum.Completed ? "PASS" : null;
            outcome.FailureReason = failureKind == MissionFailureKindEnum.TestFail ? "2 tests failed in RefundValidatorTests" : null;
            outcome.StartedUtc = startedUtc;
            outcome.EndedUtc = endedUtc;
            outcome.DurationMs = (long)(endedUtc - startedUtc).TotalMilliseconds;
            outcome.CreatedUtc = endedUtc;
            outcome.LastUpdateUtc = endedUtc;
            await db.StageOutcomes.CreateAsync(outcome).ConfigureAwait(false);
        }

        /// <summary>
        /// Spread outcomes over the last 14 days across the finished missions, with realistic success rates per model.
        /// </summary>
        /// <param name="finished">Finished missions to attach outcomes to.</param>
        /// <param name="captains">Captains.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public async Task SeedOutcomesAsync(List<Mission> finished, List<Captain> captains)
        {
            if (finished == null) throw new ArgumentNullException(nameof(finished));
            if (captains == null) throw new ArgumentNullException(nameof(captains));
            if (finished.Count == 0) return;

            Random random = new Random(7);
            MissionFailureKindEnum[] kinds = new MissionFailureKindEnum[] { MissionFailureKindEnum.TestFail, MissionFailureKindEnum.Compile, MissionFailureKindEnum.InvalidOutput, MissionFailureKindEnum.NoOp, MissionFailureKindEnum.Timeout };
            foreach (Captain captain in captains)
            {
                string persona = captain.PreferredPersona ?? PersonaCatalog.Worker;
                double success = SuccessRate(captain.Model);
                for (int i = 0; i < 30; i++)
                {
                    Mission source = finished[random.Next(finished.Count)];
                    Mission mission = new Mission(source.Title, source.Description);
                    mission.Id = source.Id;
                    mission.TenantId = _Tenant;
                    mission.UserId = _User;
                    mission.VoyageId = source.VoyageId;
                    mission.VesselId = source.VesselId;
                    mission.Persona = persona;

                    DateTime end = _Now.AddMinutes(-random.Next(60, 14 * 24 * 60));
                    DateTime start = end.AddMinutes(-random.Next(6, 45));
                    double roll = random.NextDouble();
                    if (roll < success)
                        await AddOutcomeAsync(_Db, mission, captain, 1, StageDispositionEnum.Completed, null, null, start, end).ConfigureAwait(false);
                    else if (roll < success + (1 - success) / 2)
                        await AddOutcomeAsync(_Db, mission, captain, 1, StageDispositionEnum.OutputRepair, null, ContractRepairKindEnum.CompletionLineMissing, start, end).ConfigureAwait(false);
                    else
                        await AddOutcomeAsync(_Db, mission, captain, 1, StageDispositionEnum.Escalated, kinds[random.Next(kinds.Length)], null, start, end).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Create a model profile per distinct captain model, link the captains, and attach a completed evaluation so
        /// each profile shows a tier and a measured scorecard.
        /// </summary>
        /// <param name="captains">Captains.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="captains"/> is null.</exception>
        public async Task SeedScorecardsAsync(List<Captain> captains)
        {
            if (captains == null) throw new ArgumentNullException(nameof(captains));
            foreach (Captain captain in captains)
            {
                string key = KeyOf(captain);
                if (!_Profiles.TryGetValue(key, out ModelProfile? profile))
                {
                    profile = new ModelProfile();
                    profile.TenantId = _Tenant;
                    profile.Identity = new ModelProfileIdentity { Runtime = captain.Runtime.ToString(), Provider = ProviderOf(captain), Model = captain.Model, ServedContextTokens = IsOpen(captain) ? 131072 : null };
                    profile.Tiering.Tier = captain.Tier;
                    profile.Speed.TokensPerSecond = IsOpen(captain) ? 74.0 : 52.0;
                    profile.Speed.FirstOutputMs = IsOpen(captain) ? 900 : 1600;
                    profile.Speed.MeasuredUtc = _Now.AddDays(-2);
                    profile.Notes = IsOpen(captain) ? "Self-hosted open model. Good Worker on scoped tasks; escalates to GPT-6.1 on test failures." : null;
                    profile = await _Db.ModelProfiles.CreateAsync(profile, ModelProfileRules.IdentityKey(_Tenant, profile.Identity)).ConfigureAwait(false);
                    await _Db.ModelProfiles.CreateRevisionAsync(EvaluatedRevision(profile, captain)).ConfigureAwait(false);
                    _Profiles[key] = profile;
                }

                await _Db.ModelProfiles.LinkCaptainAsync(captain.Id, profile.Id).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The profile created for a captain's model, or null.
        /// </summary>
        /// <param name="captain">Captain.</param>
        /// <returns>Profile, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="captain"/> is null.</exception>
        public ModelProfile? ProfileFor(Captain captain)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            return _Profiles.TryGetValue(KeyOf(captain), out ModelProfile? profile) ? profile : null;
        }

        #endregion

        #region Private-Methods

        private ModelProfileRevision EvaluatedRevision(ModelProfile profile, Captain captain)
        {
            bool open = IsOpen(captain);
            string persona = captain.PreferredPersona ?? PersonaCatalog.Worker;
            ModelEvaluationReport report = new ModelEvaluationReport();
            report.RunId = "demo-evaluation-" + captain.Name;
            report.Complete = true;
            report.CandidateFamily = open ? "qwen" : null;
            report.Packs = new List<string> { "csharp", "typescript" };
            report.Personas = new List<string> { persona };
            report.Probes = new List<ModelEvaluationCount>
            {
                Count("probe:tool-call", "Tool call", null, 5, 0),
                Count("probe:mcp", "MCP tools", null, 5, 0),
                Count("probe:json-output", "Structured output", null, open ? 4 : 5, open ? 1 : 0)
            };
            ModelEvaluationPersonaOutcome outcome = new ModelEvaluationPersonaOutcome();
            outcome.Persona = persona;
            outcome.Tasks = new List<ModelEvaluationCount>
            {
                Count("task:csharp/add-member", "Add a member", persona, 1, 0),
                Count("task:csharp/fix-bug", "Fix a bug", persona, 1, 0),
                Count("task:csharp/two-place-edit", "Two-place edit", persona, open ? 0 : 1, open ? 1 : 0),
                Count("task:csharp/bom-crlf", "BOM and CRLF", persona, 1, 0),
                Count("task:typescript/add-member", "Add a member", persona, 1, 0),
                Count("task:typescript/fix-bug", "Fix a bug", persona, open ? 0 : 1, open ? 1 : 0)
            };
            outcome.Totals = Count("total", "Total", persona, outcome.Tasks.Sum(t => t.Passed), outcome.Tasks.Sum(t => t.Failed));
            report.PersonaOutcomes = new List<ModelEvaluationPersonaOutcome> { outcome };
            report.ProfileId = profile.Id;
            report.CreatedUtc = _Now.AddDays(-2);

            ModelProfileRevision revision = new ModelProfileRevision();
            revision.ProfileId = profile.Id;
            revision.TenantId = _Tenant;
            revision.RuntimeVersion = open ? "openai-compatible" : "1.0";
            revision.FirstSeenUtc = _Now.AddDays(-14);
            revision.EvaluationRunId = report.RunId;
            revision.EvaluationReport = report;
            report.ProfileRevisionId = revision.Id;
            return revision;
        }

        private static ModelEvaluationCount Count(string key, string label, string? persona, int passed, int failed)
        {
            ModelEvaluationCount count = new ModelEvaluationCount();
            count.Key = key;
            count.Label = label;
            count.Persona = persona;
            count.Passed = passed;
            count.Failed = failed;
            return count;
        }

        private static string KeyOf(Captain captain)
        {
            return captain.Runtime + "|" + (captain.Model ?? "");
        }

        private static bool IsOpen(Captain captain)
        {
            return captain.Runtime == AgentRuntimeEnum.ApiEndpoint;
        }

        private static string ProviderOf(Captain captain)
        {
            if (captain.Runtime == AgentRuntimeEnum.ApiEndpoint) return "OpenAICompatible";
            if (captain.Runtime == AgentRuntimeEnum.Codex) return "OpenAI";
            return "Anthropic";
        }

        private static double SuccessRate(string? model)
        {
            if (model == null) return 0.8;
            if (model.StartsWith("qwen", StringComparison.Ordinal)) return 0.74;
            if (model.StartsWith("gpt", StringComparison.Ordinal)) return 0.9;
            if (model.Contains("opus", StringComparison.Ordinal)) return 0.94;
            return 0.86;
        }

        #endregion
    }
}
