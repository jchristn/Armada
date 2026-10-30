namespace Armada.Server
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;

    /// <summary>
    /// Caller-scoped access to model alerts, shared by REST and MCP (docs/MODEL_HETEROGENEITY.md, Alerts). Anyone signed
    /// in reads the alerts of their own tenant (a global admin reads every tenant). Evaluating, applying a suggested action
    /// and ignoring need a tenant admin of the alert's tenant or a global admin; changing the thresholds (global settings)
    /// needs a global admin. A suggested action changes something only when applied here, through the existing persona
    /// and Evaluate services, and is recorded on the alert. Throws KeyNotFound (404), Argument (400) and
    /// UnauthorizedAccess (403) exceptions.
    /// </summary>
    public class ModelAlertAccessService
    {
        #region Private-Members

        private readonly string _Header = "[ModelAlertAccessService] ";
        private readonly DatabaseDriver _Database;
        private readonly ModelAlertService _Alerts;
        private readonly Func<ModelAlertSettings> _Settings;
        private readonly Func<Task> _SaveSettings;
        private readonly ModelProbeAccessService? _Probes;
        private readonly LoggingModule _Logging;
        private readonly PersonaWriteService _Personas;
        private readonly ModelAlertActionPlanner _Planner;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="alerts">Alert evaluation service.</param>
        /// <param name="settings">Current model alert settings.</param>
        /// <param name="saveSettings">Persists the Admiral settings after a threshold change.</param>
        /// <param name="probes">Evaluate access service (for Re-evaluate), or null when Evaluate is not available.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public ModelAlertAccessService(DatabaseDriver database, ModelAlertService alerts, Func<ModelAlertSettings> settings, Func<Task> saveSettings, ModelProbeAccessService? probes, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Alerts = alerts ?? throw new ArgumentNullException(nameof(alerts));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _SaveSettings = saveSettings ?? throw new ArgumentNullException(nameof(saveSettings));
            _Probes = probes;
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Personas = new PersonaWriteService(database);
            _Planner = new ModelAlertActionPlanner(database);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// One page of alerts visible to the caller, most recently raised first, each with its suggested actions.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="query">Filters and paging, or null for the defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not signed in or has no tenant.</exception>
        public async Task<EnumerationResult<ModelAlert>> ListAsync(AuthContext caller, ModelAlertQuery? query, CancellationToken token = default)
        {
            RequirePerson(caller);
            ModelAlertQuery source = query ?? new ModelAlertQuery();
            ModelAlertQuery effective = new ModelAlertQuery
            {
                TenantId = caller.IsAdmin ? Blank(source.TenantId) : caller.TenantId,
                Status = source.Status,
                ProfileId = Blank(source.ProfileId),
                Persona = Blank(source.Persona),
                IncludeIgnored = source.IncludeIgnored,
                NotIgnoredAtUtc = source.IncludeIgnored ? null : DateTime.UtcNow,
                PageNumber = source.PageNumber,
                PageSize = source.PageSize
            };
            EnumerationResult<ModelAlert> page = await _Database.ModelAlerts.EnumerateAsync(effective, token).ConfigureAwait(false);
            foreach (ModelAlert alert in page.Objects) await AttachActionsAsync(caller, alert, token).ConfigureAwait(false);
            return page;
        }

        /// <summary>
        /// One alert visible to the caller, with its suggested actions and applied-action record.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="alertId">Alert identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The alert.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the alert does not exist or is in another tenant.</exception>
        public async Task<ModelAlert> GetAsync(AuthContext caller, string? alertId, CancellationToken token = default)
        {
            RequirePerson(caller);
            ModelAlert alert = await ReadVisibleAsync(caller, alertId, token).ConfigureAwait(false);
            await AttachActionsAsync(caller, alert, token).ConfigureAwait(false);
            return alert;
        }

        /// <summary>
        /// Run an evaluation pass now: the caller's tenant for a tenant admin, every tenant for a global admin. Reads
        /// scorecard data only; raises, keeps and resolves alerts.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The evaluation.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin.</exception>
        public async Task<ModelAlertEvaluation> EvaluateAsync(AuthContext caller, CancellationToken token = default)
        {
            RequirePerson(caller);
            RequireAdmin(caller);
            ModelAlertEvaluation evaluation = await _Alerts.EvaluateAsync(caller.IsAdmin ? null : caller.TenantId, null, token).ConfigureAwait(false);
            _Logging.Info(_Header + "evaluation by " + caller.UserId + ": " + evaluation.Pairs + " pairs, " + evaluation.Raised.Count + " raised, " + evaluation.Resolved + " resolved");
            return evaluation;
        }

        /// <summary>
        /// The alert thresholds: defaults and per-persona overrides, plus the built-in defaults where unset.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <returns>The thresholds (Defaults has every value set).</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not signed in or has no tenant.</exception>
        public ModelAlertThresholds GetThresholds(AuthContext caller)
        {
            RequirePerson(caller);
            ModelAlertThresholds current = _Settings().Thresholds;
            ModelAlertThreshold defaults = current.EffectiveFor(null);
            defaults.Persona = null;
            return new ModelAlertThresholds
            {
                Defaults = defaults,
                Overrides = current.Overrides.Select(Copy).ToList()
            };
        }

        /// <summary>
        /// Replace the alert thresholds (global settings). The next evaluation pass uses them.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="thresholds">New defaults and overrides.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored thresholds.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not a global admin.</exception>
        /// <exception cref="ArgumentException">Thrown for an invalid value.</exception>
        public async Task<ModelAlertThresholds> UpdateThresholdsAsync(AuthContext caller, ModelAlertThresholds? thresholds, CancellationToken token = default)
        {
            RequirePerson(caller);
            RequireAdmin(caller);
            if (!caller.IsAdmin) throw new UnauthorizedAccessException("Alert thresholds are server settings; only a global admin can change them.");
            if (thresholds == null) throw new ArgumentException("A request body is required.");
            string? error = thresholds.Validate();
            if (error != null) throw new ArgumentException(error);
            ModelAlertThresholds stored = new ModelAlertThresholds
            {
                Defaults = Copy(thresholds.Defaults),
                Overrides = thresholds.Overrides.Select(Copy).ToList()
            };
            stored.Defaults.Persona = null;
            foreach (ModelAlertThreshold item in stored.Overrides) item.Persona = item.Persona!.Trim();
            _Settings().Thresholds = stored;
            await _SaveSettings().ConfigureAwait(false);
            _Logging.Info(_Header + "thresholds updated by " + caller.UserId + " (" + stored.Overrides.Count + " persona overrides)");
            return GetThresholds(caller);
        }

        /// <summary>
        /// Apply one suggested action of an alert. The alert is resolved and tenant-checked before the request is
        /// validated. The action goes through the existing persona and Evaluate services and is recorded on the alert.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="alertId">Alert identifier.</param>
        /// <param name="request">Action (and days for Ignore).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record and the alert after the action.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the alert does not exist or is in another tenant.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin.</exception>
        /// <exception cref="ArgumentException">Thrown when the action is missing, unavailable or invalid.</exception>
        public async Task<ModelAlertApplyResult> ApplyAsync(AuthContext caller, string? alertId, ModelAlertApplyRequest? request, CancellationToken token = default)
        {
            RequirePerson(caller);
            ModelAlert alert = await ReadVisibleAsync(caller, alertId, token).ConfigureAwait(false);
            RequireAdmin(caller);
            if (request == null || !request.Action.HasValue) throw new ArgumentException("action is required (MoveDown, RemoveFromPersona, CompactTemplate, Reevaluate or Ignore).");
            if (!Enum.IsDefined(typeof(ModelAlertActionEnum), request.Action.Value)) throw new ArgumentException("Unknown action.");

            ModelAlertActionEnum action = request.Action.Value;
            ModelAlertActionPlan plan = await _Planner.PlanAsync(alert, caller, _Settings().DefaultIgnoreDays, token).ConfigureAwait(false);
            ModelAlertSuggestedAction suggested = plan.Actions.First(a => a.Action == action);
            if (action != ModelAlertActionEnum.Ignore && !suggested.Available)
                throw new ArgumentException(suggested.Label + " is unavailable: " + suggested.UnavailableReason);

            ModelAlertActionRecord record = new ModelAlertActionRecord { Action = action, AppliedUtc = DateTime.UtcNow, UserId = caller.UserId };
            switch (action)
            {
                case ModelAlertActionEnum.MoveDown:
                    await MoveDownAsync(caller, plan, record, token).ConfigureAwait(false);
                    break;
                case ModelAlertActionEnum.RemoveFromPersona:
                    await RemoveAsync(caller, plan, record, token).ConfigureAwait(false);
                    break;
                case ModelAlertActionEnum.Reevaluate:
                    await ReevaluateAsync(caller, plan, record, token).ConfigureAwait(false);
                    break;
                case ModelAlertActionEnum.Ignore:
                    int days = request.Days ?? _Settings().DefaultIgnoreDays;
                    if (days < 1 || days > 365) throw new ArgumentException("days must be 1 to 365.");
                    alert.IgnoredUntilUtc = record.AppliedUtc.AddDays(days);
                    record.Changed = true;
                    record.Result = "Ignored for " + days + " days, until " + alert.IgnoredUntilUtc.Value.ToString("yyyy-MM-ddTHH:mm:ssZ") + ".";
                    break;
                default:
                    throw new ArgumentException(suggested.Label + " is unavailable: " + suggested.UnavailableReason);
            }

            alert.Actions.Add(record);
            alert = await _Database.ModelAlerts.UpdateAsync(alert, token).ConfigureAwait(false);
            _Logging.Info(_Header + "alert " + alert.Id + ": " + action + " applied by " + caller.UserId + ": " + record.Result);
            await AttachActionsAsync(caller, alert, token).ConfigureAwait(false);
            return new ModelAlertApplyResult { Record = record, Alert = alert };
        }

        /// <summary>
        /// Hide an alert from the inbox for a number of days (default from settings, 7).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="alertId">Alert identifier.</param>
        /// <param name="days">Days, 1 to 365, or null for the default.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record and the alert.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the alert does not exist or is in another tenant.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin.</exception>
        /// <exception cref="ArgumentException">Thrown when days is out of range.</exception>
        public Task<ModelAlertApplyResult> IgnoreAsync(AuthContext caller, string? alertId, int? days, CancellationToken token = default)
        {
            return ApplyAsync(caller, alertId, new ModelAlertApplyRequest { Action = ModelAlertActionEnum.Ignore, Days = days }, token);
        }

        #endregion

        #region Private-Methods

        private async Task MoveDownAsync(AuthContext caller, ModelAlertActionPlan plan, ModelAlertActionRecord record, CancellationToken token)
        {
            Persona persona = plan.Persona!;
            HashSet<int> moving = new HashSet<int>(plan.EntryIndexes);
            List<PersonaCaptainPriority> reordered = new List<PersonaCaptainPriority>();
            for (int i = 0; i < persona.CaptainPriorities.Count; i++) if (!moving.Contains(i)) reordered.Add(persona.CaptainPriorities[i]);
            foreach (int i in plan.EntryIndexes) reordered.Add(persona.CaptainPriorities[i]);
            await UpdatePersonaAsync(caller, persona.Name, new PersonaUpdateRequest { CaptainPriorities = reordered }, false, token).ConfigureAwait(false);
            record.Changed = true;
            record.TargetId = persona.Name;
            record.Result = "Moved " + plan.EntryIndexes.Count + " " + persona.Name + " captain priority entr" + (plan.EntryIndexes.Count == 1 ? "y" : "ies")
                + " (" + Describe(persona, plan.EntryIndexes) + ") to the bottom of the list.";
        }

        private async Task RemoveAsync(AuthContext caller, ModelAlertActionPlan plan, ModelAlertActionRecord record, CancellationToken token)
        {
            Persona persona = plan.Persona!;
            HashSet<int> removing = new HashSet<int>(plan.EntryIndexes);
            PersonaUpdateRequest update = new PersonaUpdateRequest();
            if (removing.Count > 0)
                update.CaptainPriorities = persona.CaptainPriorities.Where((p, i) => !removing.Contains(i)).ToList();
            if (plan.DefaultCaptainOnProfile) update.DefaultCaptainId = null;
            await UpdatePersonaAsync(caller, persona.Name, update, plan.DefaultCaptainOnProfile, token).ConfigureAwait(false);
            List<string> parts = new List<string>();
            if (removing.Count > 0) parts.Add("removed " + removing.Count + " captain priority entr" + (removing.Count == 1 ? "y" : "ies") + " (" + Describe(persona, plan.EntryIndexes) + ")");
            if (plan.DefaultCaptainOnProfile) parts.Add("cleared the default captain " + persona.DefaultCaptainId);
            record.Changed = true;
            record.TargetId = persona.Name;
            record.Result = persona.Name + ": " + String.Join("; ", parts) + ".";
        }

        private async Task ReevaluateAsync(AuthContext caller, ModelAlertActionPlan plan, ModelAlertActionRecord record, CancellationToken token)
        {
            if (_Probes == null) throw new ArgumentException("Evaluate is not available on this server.");
            Captain captain = plan.ProfileCaptains[0];
            ModelProbeRun run = await _Probes.EvaluateCaptainAsync(caller, captain.Id, ModelProbeDepthEnum.Quick, token).ConfigureAwait(false);
            record.Changed = true;
            record.TargetId = run.Id;
            record.Result = "Started Quick Evaluate " + run.Id + " of captain " + captain.Name + " (" + captain.Id + "); status " + run.Status + ".";
        }

        private async Task UpdatePersonaAsync(AuthContext caller, string name, PersonaUpdateRequest update, bool defaultCaptainIdPresent, CancellationToken token)
        {
            PersonaWriteResult result = await _Personas.UpdateAsync(caller, name, update, defaultCaptainIdPresent, token).ConfigureAwait(false);
            switch (result.Status)
            {
                case PersonaWriteStatusEnum.Ok:
                    return;
                case PersonaWriteStatusEnum.NotFound:
                    throw new KeyNotFoundException(result.Message);
                case PersonaWriteStatusEnum.Forbidden:
                    throw new UnauthorizedAccessException(result.Message);
                default:
                    throw new ArgumentException(result.Message);
            }
        }

        private static string Describe(Persona persona, List<int> indexes)
        {
            return String.Join(", ", indexes.Select(i =>
            {
                PersonaCaptainPriority p = persona.CaptainPriorities[i];
                return (p.Runtime.HasValue ? p.Runtime.Value + " " : "") + p.Model + (String.IsNullOrEmpty(p.Endpoint) ? "" : " @" + p.Endpoint);
            }));
        }

        private async Task AttachActionsAsync(AuthContext caller, ModelAlert alert, CancellationToken token)
        {
            alert.SuggestedActions = (await _Planner.PlanAsync(alert, caller, _Settings().DefaultIgnoreDays, token).ConfigureAwait(false)).Actions;
        }

        private async Task<ModelAlert> ReadVisibleAsync(AuthContext caller, string? alertId, CancellationToken token)
        {
            string? id = Blank(alertId);
            ModelAlert? alert = id == null ? null : await _Database.ModelAlerts.ReadAsync(id, token).ConfigureAwait(false);
            if (alert == null || !CanSeeTenant(caller, alert.TenantId)) throw new KeyNotFoundException("Model alert not found.");
            return alert;
        }

        private static ModelAlertThreshold Copy(ModelAlertThreshold source)
        {
            ModelAlertThreshold s = source ?? new ModelAlertThreshold();
            return new ModelAlertThreshold
            {
                Persona = s.Persona,
                FirstPassMinRate = s.FirstPassMinRate,
                FirstPassMinMissions = s.FirstPassMinMissions,
                EscalationsInARow = s.EscalationsInARow,
                ProtocolRepairMaxRate = s.ProtocolRepairMaxRate,
                ProtocolRepairMinAttempts = s.ProtocolRepairMinAttempts,
                CostMedianMultiplier = s.CostMedianMultiplier,
                CostMinAccepted = s.CostMinAccepted
            };
        }

        private static void RequirePerson(AuthContext caller)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (!caller.IsAuthenticated) throw new UnauthorizedAccessException("Authentication required.");
            if (!caller.IsAdmin && String.IsNullOrEmpty(caller.TenantId)) throw new UnauthorizedAccessException("A tenant is required.");
        }

        private static void RequireAdmin(AuthContext caller)
        {
            if (!String.IsNullOrEmpty(caller.MissionId) || !String.IsNullOrEmpty(caller.AskThreadId))
                throw new UnauthorizedAccessException("A captain session cannot act on model alerts.");
            if (!caller.IsAdmin && !caller.IsTenantAdmin) throw new UnauthorizedAccessException("Only an admin can act on model alerts.");
        }

        private static bool CanSeeTenant(AuthContext caller, string? tenantId)
        {
            if (caller.IsAdmin) return true;
            return !String.IsNullOrEmpty(tenantId) && String.Equals(caller.TenantId, tenantId, StringComparison.Ordinal);
        }

        private static string? Blank(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}
