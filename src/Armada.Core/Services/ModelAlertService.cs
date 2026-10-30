namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Evaluates the scorecard of every (model profile, persona) with recent production attempts against the alert
    /// thresholds (docs/MODEL_HETEROGENEITY.md, Alerts, Decision 13), on a periodic background pass and on demand. Raises
    /// one alert per (profile, revision group, persona, vessel scope, rule), keeps it while the condition holds and
    /// resolves it when a later pass finds it no longer holds. It only reads scorecard data and writes alert rows: it never
    /// changes captains, personas, priorities, templates or routing.
    /// </summary>
    public class ModelAlertService
    {
        #region Public-Members

        /// <summary>
        /// Called for every alert raised for the first time or reopened that is not ignored (for the optional push).
        /// </summary>
        public Action<ModelAlert>? AlertRaised { get; set; } = null;

        /// <summary>
        /// The most recent completed pass, or null.
        /// </summary>
        public ModelAlertEvaluation? LastEvaluation { get; private set; } = null;

        #endregion

        #region Private-Members

        private const int OutcomePageSize = 1000;
        private const int MaxOutcomePages = 20;
        private readonly string _Header = "[ModelAlertService] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;
        private readonly Func<ModelAlertSettings> _Settings;
        private readonly ModelScorecardService _Scorecards;
        private readonly SemaphoreSlim _Pass = new SemaphoreSlim(1, 1);
        private CancellationTokenSource? _LoopToken = null;
        private Task? _Loop = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="settings">Current model alert settings (read on every pass).</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ModelAlertService(DatabaseDriver database, LoggingModule logging, Func<ModelAlertSettings> settings)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Scorecards = new ModelScorecardService(database, logging);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the periodic pass (every <see cref="ModelAlertSettings.IntervalMinutes"/>, skipped while
        /// <see cref="ModelAlertSettings.Enabled"/> is false). The first pass runs one interval after start.
        /// </summary>
        public void Start()
        {
            if (_Loop != null) return;
            _LoopToken = new CancellationTokenSource();
            CancellationToken token = _LoopToken.Token;
            _Loop = Task.Run(() => LoopAsync(token));
        }

        /// <summary>
        /// Stop the periodic pass.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task StopAsync()
        {
            if (_LoopToken == null || _Loop == null) return;
            _LoopToken.Cancel();
            try
            {
                await _Loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            _LoopToken.Dispose();
            _LoopToken = null;
            _Loop = null;
        }

        /// <summary>
        /// Run one evaluation pass. Passes are serialized.
        /// </summary>
        /// <param name="tenantId">Tenant to evaluate, or null for every tenant.</param>
        /// <param name="nowUtc">Evaluation time, or null for now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What was evaluated and the alerts raised, kept and resolved.</returns>
        public async Task<ModelAlertEvaluation> EvaluateAsync(string? tenantId, DateTime? nowUtc = null, CancellationToken token = default)
        {
            await _Pass.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ModelAlertEvaluation evaluation = await EvaluateLockedAsync(tenantId, (nowUtc ?? DateTime.UtcNow).ToUniversalTime(), token).ConfigureAwait(false);
                LastEvaluation = evaluation;
                return evaluation;
            }
            finally
            {
                _Pass.Release();
            }
        }

        #endregion

        #region Private-Methods

        private async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(_Settings().IntervalMinutes), token).ConfigureAwait(false);
                if (!_Settings().Enabled) continue;
                try
                {
                    ModelAlertEvaluation result = await EvaluateAsync(null, null, token).ConfigureAwait(false);
                    _Logging.Info(_Header + "pass: " + result.Pairs + " pairs, " + result.Raised.Count + " raised, " + result.StillOpen + " still open, " + result.Resolved + " resolved");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "evaluation pass failed: " + ex.ToString());
                }
            }
        }

        private async Task<ModelAlertEvaluation> EvaluateLockedAsync(string? tenantId, DateTime now, CancellationToken token)
        {
            ModelAlertSettings settings = _Settings();
            ModelAlertEvaluation evaluation = new ModelAlertEvaluation { EvaluatedUtc = now, TenantId = tenantId };

            // (profile, persona) pairs with production attempts in the default scorecard window.
            List<StageOutcome> outcomes = new List<StageOutcome>();
            StageOutcomeQuery query = new StageOutcomeQuery
            {
                TenantId = tenantId,
                IsEvaluation = false,
                FromUtc = now.AddDays(-ModelScorecardRequest.DefaultWindowDays),
                ToUtc = now,
                PageSize = OutcomePageSize
            };
            for (int page = 1; page <= MaxOutcomePages; page++)
            {
                query.PageNumber = page;
                EnumerationResult<StageOutcome> result = await _Database.StageOutcomes.EnumerateAsync(query, token).ConfigureAwait(false);
                outcomes.AddRange(result.Objects);
                if (result.Objects.Count == 0 || outcomes.Count >= result.TotalRecords) break;
                if (page == MaxOutcomePages) evaluation.Truncated = true;
            }

            Dictionary<string, ModelProfile?> profilesByKey = new Dictionary<string, ModelProfile?>(StringComparer.Ordinal);
            Dictionary<string, Pair> pairs = new Dictionary<string, Pair>(StringComparer.Ordinal);
            foreach (StageOutcome outcome in outcomes)
            {
                string persona = PersonaCatalog.NormalizeName(outcome.Persona);
                if (persona.Length == 0) continue;
                ModelProfileObservation? observation = ModelProfileService.ObservationFrom(outcome);
                if (observation == null) continue;
                string identityKey = ModelProfileRules.IdentityKey(outcome.TenantId, ModelProfileRules.IdentityOf(observation));
                if (!profilesByKey.TryGetValue(identityKey, out ModelProfile? profile))
                {
                    profile = await _Database.ModelProfiles.ReadByIdentityKeyAsync(identityKey, token).ConfigureAwait(false);
                    profilesByKey[identityKey] = profile;
                }
                if (profile == null) continue;
                string pairKey = profile.Id + "\n" + persona.ToLowerInvariant();
                if (!pairs.TryGetValue(pairKey, out Pair? pair))
                {
                    pair = new Pair { Profile = profile, Persona = persona };
                    pairs[pairKey] = pair;
                }
                if (!String.IsNullOrEmpty(outcome.VesselId)) pair.VesselIds.Add(outcome.VesselId!);
            }
            evaluation.Pairs = pairs.Count;

            // Scorecards: every vessel together, and each vessel when configured.
            List<Card> cards = new List<Card>();
            foreach (Pair pair in pairs.Values.OrderBy(p => p.Profile.Id, StringComparer.Ordinal).ThenBy(p => p.Persona, StringComparer.OrdinalIgnoreCase))
            {
                List<string?> scopes = new List<string?> { null };
                if (settings.IncludeVessels) scopes.AddRange(pair.VesselIds.OrderBy(v => v, StringComparer.Ordinal));
                foreach (string? vesselId in scopes)
                {
                    try
                    {
                        ModelScorecard card = await _Scorecards.ComputeAsync(pair.Profile, new ModelScorecardRequest { Persona = pair.Persona, VesselId = vesselId }, now, token).ConfigureAwait(false);
                        if (card.Truncated) evaluation.Truncated = true;
                        cards.Add(new Card { Profile = pair.Profile, Persona = pair.Persona, VesselId = vesselId, Scorecard = card });
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        _Logging.Warn(_Header + "scorecard of " + pair.Profile.Id + " as " + pair.Persona + " failed: " + ex.Message);
                    }
                }
            }

            // Rules, then raise or keep the crossed conditions.
            Dictionary<string, string?> vesselNames = new Dictionary<string, string?>(StringComparer.Ordinal);
            HashSet<string> keptOpen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Card card in cards)
            {
                ModelAlertThreshold thresholds = settings.Thresholds.EffectiveFor(card.Persona);
                int minAccepted = thresholds.CostMinAccepted ?? ModelAlertThreshold.DefaultCostMinAccepted;
                List<double> otherCosts = cards
                    .Where(c => !String.Equals(c.Profile.Id, card.Profile.Id, StringComparison.Ordinal)
                        && String.Equals(c.Profile.TenantId, card.Profile.TenantId, StringComparison.Ordinal)
                        && PersonaCatalog.Matches(c.Persona, card.Persona)
                        && String.Equals(c.VesselId, card.VesselId, StringComparison.Ordinal))
                    .SelectMany(c => c.Scorecard.Groups)
                    .Select(g => ModelAlertRules.QualifyingCost(g, minAccepted))
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();

                string? vesselName = null;
                if (card.VesselId != null)
                {
                    if (!vesselNames.TryGetValue(card.VesselId, out vesselName))
                    {
                        vesselName = (await _Database.Vessels.ReadAsync(card.VesselId, token).ConfigureAwait(false))?.Name;
                        vesselNames[card.VesselId] = vesselName;
                    }
                }

                foreach (ModelScorecardGroup group in card.Scorecard.Groups)
                {
                    if (group.Attempts == 0) continue;
                    ModelAlertTarget target = new ModelAlertTarget
                    {
                        TenantId = card.Profile.TenantId,
                        ProfileId = card.Profile.Id,
                        Model = card.Profile.Identity.Model,
                        RevisionGroup = ModelAlertRules.GroupName(group),
                        Persona = card.Persona,
                        VesselId = card.VesselId,
                        VesselName = vesselName,
                        Missions = group.Missions,
                        Attempts = group.Attempts,
                        Rules = ModelAlertRules.Evaluate(group, thresholds, otherCosts)
                    };
                    evaluation.Targets.Add(target);

                    foreach (ModelAlertRuleResult rule in target.Rules.Where(r => r.State == ModelAlertRuleStateEnum.Crossed))
                    {
                        string key = ModelAlertRules.Key(target.TenantId, target.ProfileId, target.RevisionGroup, target.Persona, target.VesselId, rule.Rule);
                        ModelAlert? alert = await _Database.ModelAlerts.ReadByKeyAsync(key, token).ConfigureAwait(false);
                        bool raised = false;
                        if (alert == null)
                        {
                            alert = new ModelAlert
                            {
                                TenantId = target.TenantId,
                                ProfileId = target.ProfileId,
                                RevisionGroup = target.RevisionGroup,
                                Persona = target.Persona,
                                VesselId = target.VesselId,
                                Rule = rule.Rule,
                                CreatedUtc = now,
                                LastRaisedUtc = now
                            };
                            Fill(alert, target, rule, now);
                            alert = await _Database.ModelAlerts.CreateAsync(alert, key, token).ConfigureAwait(false);
                            raised = true;
                        }
                        else
                        {
                            if (alert.Status == ModelAlertStatusEnum.Resolved)
                            {
                                alert.Status = ModelAlertStatusEnum.Open;
                                alert.ResolvedUtc = null;
                                alert.LastRaisedUtc = now;
                                raised = true;
                            }
                            else
                            {
                                evaluation.StillOpen++;
                            }
                            Fill(alert, target, rule, now);
                            alert = await _Database.ModelAlerts.UpdateAsync(alert, token).ConfigureAwait(false);
                        }
                        keptOpen.Add(alert.Id);
                        if (raised)
                        {
                            evaluation.Raised.Add(alert);
                            if (!alert.IsIgnored(now)) Notify(alert);
                        }
                    }
                }
            }

            // Resolve the open conditions that no longer hold (in the evaluated tenant).
            List<ModelAlert> open = await _Database.ModelAlerts.EnumerateOpenAsync(tenantId, token).ConfigureAwait(false);
            foreach (ModelAlert alert in open)
            {
                if (keptOpen.Contains(alert.Id)) continue;
                alert.Status = ModelAlertStatusEnum.Resolved;
                alert.ResolvedUtc = now;
                alert.LastEvaluatedUtc = now;
                await _Database.ModelAlerts.UpdateAsync(alert, token).ConfigureAwait(false);
                evaluation.Resolved++;
            }

            return evaluation;
        }

        private static void Fill(ModelAlert alert, ModelAlertTarget target, ModelAlertRuleResult rule, DateTime now)
        {
            alert.Model = target.Model;
            alert.VesselName = target.VesselName;
            alert.Title = ModelAlertRules.Title(target, rule);
            alert.Detail = ModelAlertRules.Detail(target, rule);
            alert.Count = rule.Count;
            alert.Of = rule.Of;
            alert.Value = rule.Value;
            alert.Threshold = rule.Threshold;
            alert.LastEvaluatedUtc = now;
        }

        private void Notify(ModelAlert alert)
        {
            Action<ModelAlert>? callback = AlertRaised;
            if (callback == null) return;
            try
            {
                callback(alert);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "alert notification failed for " + alert.Id + ": " + ex.Message);
            }
        }

        #endregion

        #region Private-Classes

        private sealed class Pair
        {
            public ModelProfile Profile { get; set; } = new ModelProfile();
            public string Persona { get; set; } = "";
            public HashSet<string> VesselIds { get; } = new HashSet<string>(StringComparer.Ordinal);
        }

        private sealed class Card
        {
            public ModelProfile Profile { get; set; } = new ModelProfile();
            public string Persona { get; set; } = "";
            public string? VesselId { get; set; } = null;
            public ModelScorecard Scorecard { get; set; } = new ModelScorecard();
        }

        #endregion
    }
}
