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

    /// <summary>
    /// The caller-scoped API over stage outcomes and failure-cause rules, shared by REST and MCP so both enforce the
    /// same rules. Outcomes: a global admin sees every tenant; everyone else sees their own tenant's outcomes and may
    /// classify them by hand; re-applying the rules needs a global admin or a tenant admin (limited to their tenant).
    /// Rules: callers see their tenant's rules plus the rules for every tenant (TenantId null); anyone in the tenant
    /// may create, edit and delete the tenant's rules; rules for every tenant and built-in rules need a global admin.
    /// Captain sessions (mission- or thread-scoped tokens) are refused. Errors are typed exceptions:
    /// <see cref="KeyNotFoundException"/> (missing or outside the caller's scope), <see cref="ArgumentException"/>
    /// (invalid input), <see cref="UnauthorizedAccessException"/> (not allowed), <see cref="InvalidOperationException"/>
    /// (a state conflict) and <see cref="DuplicateEntityException"/> (a rule name already used in the tenant).
    /// </summary>
    public class StageOutcomeAccessService
    {
        #region Public-Members

        /// <summary>
        /// Priority given to a rule drafted from an outcome: ahead of the built-in rules (10 and up), because a rule
        /// built from one outcome's signals is narrower than any of them.
        /// </summary>
        public const int DraftRulePriority = 5;

        /// <summary>
        /// Longest rule description kept.
        /// </summary>
        public const int MaxDescriptionLength = 1000;

        #endregion

        #region Private-Members

        private string _Header = "[StageOutcomeAccessService] ";
        private readonly DatabaseDriver _Database;
        private readonly StageOutcomeService _Service;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="service">Stage outcome service.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public StageOutcomeAccessService(DatabaseDriver database, StageOutcomeService service, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Service = service ?? throw new ArgumentNullException(nameof(service));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// One page of the outcomes visible to the caller, newest first. A caller other than a global admin is limited to
        /// their own tenant whatever TenantId the query names.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="query">Filters and paging, or null for the first page of everything visible.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="ArgumentException">Thrown when FromUtc is after ToUtc.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown for a captain session or a caller without a tenant.</exception>
        public async Task<EnumerationResult<StageOutcome>> ListOutcomesAsync(AuthContext caller, StageOutcomeQuery? query, CancellationToken token = default)
        {
            RequirePerson(caller);
            StageOutcomeQuery effective = Scope(caller, query);
            if (effective.PageNumber < 1) effective.PageNumber = 1;
            if (effective.PageSize < 1) effective.PageSize = 1;
            if (effective.PageSize > 1000) effective.PageSize = 1000;
            return await _Database.StageOutcomes.EnumerateAsync(effective, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read one outcome visible to the caller.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="outcomeId">Outcome identifier (sto_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The outcome.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the outcome is missing or in another tenant.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown for a captain session or a caller without a tenant.</exception>
        public async Task<StageOutcome> GetOutcomeAsync(AuthContext caller, string? outcomeId, CancellationToken token = default)
        {
            RequirePerson(caller);
            if (String.IsNullOrWhiteSpace(outcomeId)) throw new KeyNotFoundException("Stage outcome not found.");
            StageOutcome? outcome = await _Database.StageOutcomes.ReadAsync(outcomeId.Trim(), token).ConfigureAwait(false);
            if (outcome == null || !CanSeeTenant(caller, outcome.TenantId)) throw new KeyNotFoundException("Stage outcome not found.");
            return outcome;
        }

        /// <summary>
        /// Classify an outcome by hand (records the caller as the classifier), or with a null cause hand it back to the
        /// rules.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="outcomeId">Outcome identifier.</param>
        /// <param name="request">Cause (null to hand back to the rules) and optional note.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated outcome.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the cause is None or not a known value.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the outcome is missing or in another tenant.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown for a captain session or a caller without a tenant.</exception>
        public async Task<StageOutcome> ClassifyAsync(AuthContext caller, string? outcomeId, StageOutcomeClassifyRequest? request, CancellationToken token = default)
        {
            StageOutcome outcome = await GetOutcomeAsync(caller, outcomeId, token).ConfigureAwait(false);
            StageOutcomeClassifyRequest body = request ?? new StageOutcomeClassifyRequest();
            if (body.Cause.HasValue)
            {
                if (!Enum.IsDefined(typeof(FailureCauseEnum), body.Cause.Value)) throw new ArgumentException("Cause is not a known value.", nameof(request));
                if (body.Cause.Value == FailureCauseEnum.None)
                    throw new ArgumentException("Cause None is not a classification; send a null cause to hand the outcome back to the rules.", nameof(request));
            }

            StageOutcome? updated = await _Service.ClassifyManuallyAsync(outcome.Id, body.Cause, body.Note, caller.UserId, token).ConfigureAwait(false);
            if (updated == null) throw new KeyNotFoundException("Stage outcome not found.");
            _Logging.Info(_Header + "outcome " + updated.Id + " classified " + (body.Cause.HasValue ? body.Cause.Value.ToString() : "by the rules (" + updated.SuspectedCause + ")") + " by " + caller.UserId);
            return updated;
        }

        /// <summary>
        /// Re-apply the rules to every outcome matching the filter (paging is ignored); outcomes classified by hand are
        /// left alone. Needs a global admin, or a tenant admin (limited to their tenant).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="filter">Which outcomes, or null for every outcome in scope.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What was examined and changed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="ArgumentException">Thrown when FromUtc is after ToUtc.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin.</exception>
        public async Task<StageOutcomeReclassifyResult> ReclassifyAsync(AuthContext caller, StageOutcomeQuery? filter, CancellationToken token = default)
        {
            RequirePerson(caller);
            if (!caller.IsAdmin && !caller.IsTenantAdmin) throw new UnauthorizedAccessException("Only an admin can re-apply the failure-cause rules.");
            StageOutcomeQuery effective = Scope(caller, filter);
            StageOutcomeReclassifyResult result = await _Service.ReclassifyAsync(effective, token).ConfigureAwait(false);
            _Logging.Info(_Header + "rules re-applied by " + caller.UserId + " for tenant " + (effective.TenantId ?? "(all)") + ": examined " + result.Examined + ", changed " + result.Changed + ", manual " + result.SkippedManual);
            return result;
        }

        /// <summary>
        /// Draft (without storing) a failure-cause rule from an outcome's cause and signals: its failure kind, runtime
        /// failure kind, stage-check status and reason, disposition and persona. The caller edits the draft and creates
        /// it with <see cref="CreateRuleAsync"/>.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="outcomeId">Outcome identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The unsaved draft, for the outcome's tenant.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the outcome has no cause (None).</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the outcome is missing or in another tenant.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown for a captain session or a caller without a tenant.</exception>
        public async Task<FailureCauseRule> DraftRuleAsync(AuthContext caller, string? outcomeId, CancellationToken token = default)
        {
            StageOutcome outcome = await GetOutcomeAsync(caller, outcomeId, token).ConfigureAwait(false);
            if (outcome.SuspectedCause == FailureCauseEnum.None)
                throw new InvalidOperationException("Stage outcome " + outcome.Id + " has no cause to turn into a rule; classify it first.");

            FailureCauseRuleMatch match = new FailureCauseRuleMatch();
            if (!String.IsNullOrWhiteSpace(outcome.Persona)) match.Personas.Add(outcome.Persona!.Trim());
            if (outcome.FailureKind.HasValue) match.FailureKinds.Add(outcome.FailureKind.Value);
            if (outcome.RuntimeFailureKind.HasValue) match.RuntimeFailureKinds.Add(outcome.RuntimeFailureKind.Value);
            if (outcome.StageCheckStatus.HasValue) match.StageCheckStatuses.Add(outcome.StageCheckStatus.Value);
            if (outcome.StageCheckReason.HasValue && outcome.StageCheckReason.Value != StageCheckReasonEnum.None) match.StageCheckReasons.Add(outcome.StageCheckReason.Value);
            match.Dispositions.Add(outcome.Disposition);

            string description = "Drafted from stage outcome " + outcome.Id + " (mission " + outcome.MissionId + ").";
            if (!String.IsNullOrWhiteSpace(outcome.CauseNote)) description += " Note: " + outcome.CauseNote!.Trim();

            FailureCauseRule draft = new FailureCauseRule();
            draft.TenantId = outcome.TenantId;
            draft.Name = outcome.SuspectedCause + " like " + outcome.Id;
            draft.Description = Clip(description, MaxDescriptionLength);
            draft.Priority = DraftRulePriority;
            draft.Enabled = true;
            draft.IsBuiltIn = false;
            draft.Match = match;
            draft.Cause = outcome.SuspectedCause;
            draft.CreatedByUserId = caller.UserId;
            return draft;
        }

        /// <summary>
        /// One page of the rules visible to the caller in evaluation order (Priority ascending, then Name): the caller's
        /// tenant plus the rules for every tenant; a global admin sees every rule.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="page">Paging, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown for a captain session or a caller without a tenant.</exception>
        public async Task<EnumerationResult<FailureCauseRule>> ListRulesAsync(AuthContext caller, EnumerationQuery? page, CancellationToken token = default)
        {
            RequirePerson(caller);
            // Paged in the database (COUNT over the same tenant scope), never loaded whole and sliced.
            return await _Database.FailureCauseRules.EnumeratePageAsync(caller.IsAdmin ? null : caller.TenantId, page ?? new EnumerationQuery(), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read one rule visible to the caller.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="ruleId">Rule identifier (fcr_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The rule.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the rule is missing or belongs to another tenant.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown for a captain session or a caller without a tenant.</exception>
        public async Task<FailureCauseRule> GetRuleAsync(AuthContext caller, string? ruleId, CancellationToken token = default)
        {
            RequirePerson(caller);
            if (String.IsNullOrWhiteSpace(ruleId)) throw new KeyNotFoundException("Failure-cause rule not found.");
            FailureCauseRule? rule = await _Database.FailureCauseRules.ReadAsync(ruleId.Trim(), token).ConfigureAwait(false);
            if (rule == null) throw new KeyNotFoundException("Failure-cause rule not found.");
            if (rule.TenantId != null && !CanSeeTenant(caller, rule.TenantId)) throw new KeyNotFoundException("Failure-cause rule not found.");
            return rule;
        }

        /// <summary>
        /// Create a rule. A global admin may set TenantId (null applies the rule to every tenant); anyone else creates a
        /// rule for their own tenant. IsBuiltIn is always false.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="rule">Name, Description, Priority, Enabled, Match, Cause, and (global admins) TenantId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created rule.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the rule is invalid (<see cref="FailureCauseClassifier.Validate"/>).</exception>
        /// <exception cref="DuplicateEntityException">Thrown when the tenant already has a rule with that name.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown for a captain session or a caller without a tenant.</exception>
        public async Task<FailureCauseRule> CreateRuleAsync(AuthContext caller, FailureCauseRule? rule, CancellationToken token = default)
        {
            RequirePerson(caller);
            if (rule == null) throw new ArgumentException("A rule is required.", nameof(rule));

            FailureCauseRule created = new FailureCauseRule();
            created.TenantId = caller.IsAdmin ? Blank(rule.TenantId) : caller.TenantId;
            created.IsBuiltIn = false;
            created.CreatedByUserId = caller.UserId;
            CopyEditable(rule, created);
            Validate(created);
            await RequireUniqueNameAsync(created, token).ConfigureAwait(false);

            created = await _Database.FailureCauseRules.CreateAsync(created, token).ConfigureAwait(false);
            _Logging.Info(_Header + "rule " + created.Id + " (" + created.Name + ") created by " + caller.UserId + " for tenant " + (created.TenantId ?? "(all)"));
            return created;
        }

        /// <summary>
        /// Replace a rule's Name, Description, Priority, Enabled, Match and Cause. Its tenant and built-in flag are fixed.
        /// Rules for every tenant and built-in rules need a global admin; an edited built-in rule is never overwritten by
        /// seeding.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="ruleId">Rule identifier.</param>
        /// <param name="update">New values.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated rule.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the result is invalid.</exception>
        /// <exception cref="DuplicateEntityException">Thrown when another rule of the tenant has that name.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the rule is missing or belongs to another tenant.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller may not edit the rule.</exception>
        public async Task<FailureCauseRule> UpdateRuleAsync(AuthContext caller, string? ruleId, FailureCauseRule? update, CancellationToken token = default)
        {
            FailureCauseRule rule = await GetRuleAsync(caller, ruleId, token).ConfigureAwait(false);
            RequireRuleWriter(caller, rule, "edit");
            if (update == null) throw new ArgumentException("A rule is required.", nameof(update));

            CopyEditable(update, rule);
            Validate(rule);
            await RequireUniqueNameAsync(rule, token).ConfigureAwait(false);

            FailureCauseRule updated = await _Database.FailureCauseRules.UpdateAsync(rule, token).ConfigureAwait(false);
            _Logging.Info(_Header + "rule " + updated.Id + " (" + updated.Name + ") updated by " + caller.UserId);
            return updated;
        }

        /// <summary>
        /// Delete a rule. Rules for every tenant and built-in rules need a global admin. A deleted built-in rule is stored
        /// again the next time the Admiral starts (seeding restores missing built-ins by name); disable it instead to keep
        /// it out of classification.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="ruleId">Rule identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The deleted rule.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the rule is missing or belongs to another tenant.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller may not delete the rule.</exception>
        public async Task<FailureCauseRule> DeleteRuleAsync(AuthContext caller, string? ruleId, CancellationToken token = default)
        {
            FailureCauseRule rule = await GetRuleAsync(caller, ruleId, token).ConfigureAwait(false);
            RequireRuleWriter(caller, rule, "delete");
            await _Database.FailureCauseRules.DeleteAsync(rule.Id, token).ConfigureAwait(false);
            _Logging.Info(_Header + "rule " + rule.Id + " (" + rule.Name + ") deleted by " + caller.UserId);
            return rule;
        }

        #endregion

        #region Private-Methods

        private static void RequirePerson(AuthContext caller)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (!caller.IsAuthenticated) throw new UnauthorizedAccessException("Authentication required.");
            if (!String.IsNullOrEmpty(caller.MissionId) || !String.IsNullOrEmpty(caller.AskThreadId))
                throw new UnauthorizedAccessException("A captain session cannot read or change stage outcomes or failure-cause rules.");
            if (!caller.IsAdmin && String.IsNullOrEmpty(caller.TenantId)) throw new UnauthorizedAccessException("A tenant is required.");
        }

        private static bool CanSeeTenant(AuthContext caller, string? tenantId)
        {
            if (caller.IsAdmin) return true;
            return !String.IsNullOrEmpty(tenantId) && String.Equals(caller.TenantId, tenantId, StringComparison.Ordinal);
        }

        private static void RequireRuleWriter(AuthContext caller, FailureCauseRule rule, string verb)
        {
            if (caller.IsAdmin) return;
            if (rule.IsBuiltIn) throw new UnauthorizedAccessException("Only a global admin can " + verb + " a built-in failure-cause rule.");
            if (rule.TenantId == null) throw new UnauthorizedAccessException("Only a global admin can " + verb + " a failure-cause rule for every tenant.");
        }

        private static StageOutcomeQuery Scope(AuthContext caller, StageOutcomeQuery? query)
        {
            StageOutcomeQuery source = query ?? new StageOutcomeQuery();
            if (source.FromUtc.HasValue && source.ToUtc.HasValue && source.FromUtc.Value > source.ToUtc.Value)
                throw new ArgumentException("fromUtc must not be after toUtc.", nameof(query));

            StageOutcomeQuery effective = new StageOutcomeQuery
            {
                TenantId = caller.IsAdmin ? Blank(source.TenantId) : caller.TenantId,
                MissionId = Blank(source.MissionId),
                VoyageId = Blank(source.VoyageId),
                VesselId = Blank(source.VesselId),
                CaptainId = Blank(source.CaptainId),
                Persona = Blank(source.Persona),
                Model = Blank(source.Model),
                SuspectedCause = source.SuspectedCause,
                CauseSource = source.CauseSource,
                Disposition = source.Disposition,
                IsEvaluation = source.IsEvaluation,
                FromUtc = source.FromUtc,
                ToUtc = source.ToUtc,
                PageNumber = source.PageNumber,
                PageSize = source.PageSize
            };
            return effective;
        }

        private static void CopyEditable(FailureCauseRule source, FailureCauseRule target)
        {
            target.Name = (source.Name ?? String.Empty).Trim();
            target.Description = String.IsNullOrWhiteSpace(source.Description) ? null : Clip(source.Description!.Trim(), MaxDescriptionLength);
            target.Priority = source.Priority;
            target.Enabled = source.Enabled;
            target.Match = source.Match!;
            target.Cause = source.Cause;
            if (target.Match == null) return;
            if (target.Match.TextPattern != null && String.IsNullOrWhiteSpace(target.Match.TextPattern)) target.Match.TextPattern = null;
            // A list sent as null deserializes to null; the classifier expects empty lists.
            if (target.Match.Runtimes == null) target.Match.Runtimes = new List<AgentRuntimeEnum>();
            if (target.Match.FailureKinds == null) target.Match.FailureKinds = new List<MissionFailureKindEnum>();
            if (target.Match.RuntimeFailureKinds == null) target.Match.RuntimeFailureKinds = new List<RuntimeFailureKindEnum>();
            if (target.Match.StageCheckStatuses == null) target.Match.StageCheckStatuses = new List<StageCheckStatusEnum>();
            if (target.Match.StageCheckReasons == null) target.Match.StageCheckReasons = new List<StageCheckReasonEnum>();
            if (target.Match.Dispositions == null) target.Match.Dispositions = new List<StageDispositionEnum>();
            if (target.Match.Personas == null) target.Match.Personas = new List<string>();
            else target.Match.Personas = target.Match.Personas.Where(p => !String.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
        }

        private static void Validate(FailureCauseRule rule)
        {
            string? problem = FailureCauseClassifier.Validate(rule);
            if (problem != null) throw new ArgumentException(problem, nameof(rule));
        }

        private async Task RequireUniqueNameAsync(FailureCauseRule rule, CancellationToken token)
        {
            List<FailureCauseRule> existing = await _Database.FailureCauseRules.EnumerateAsync(rule.TenantId, token).ConfigureAwait(false);
            bool taken = existing.Any(r =>
                String.Equals(r.TenantId, rule.TenantId, StringComparison.Ordinal)
                && !String.Equals(r.Id, rule.Id, StringComparison.Ordinal)
                && String.Equals(r.Name, rule.Name, StringComparison.OrdinalIgnoreCase));
            if (taken)
                throw new DuplicateEntityException("FailureCauseRule", "Name", rule.Name, "A failure-cause rule named '" + rule.Name + "' already exists " + (rule.TenantId == null ? "for every tenant." : "in this tenant."));
        }

        private static string? Blank(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string Clip(string value, int max)
        {
            return value.Length <= max ? value : value.Substring(0, max);
        }

        #endregion
    }
}
