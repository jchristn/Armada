namespace Armada.Core.Database.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Recovery outcome persistence shared by the four providers. Thread-safe.
    /// </summary>
    public abstract class BlackBoxRecoveryOutcomeMethodsBase : IBlackBoxRecoveryOutcomeMethods
    {
        #region Private-Members

        private readonly BlackBoxSqlDialect _Sql;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="sql">Provider dialect.</param>
        /// <exception cref="ArgumentNullException">Thrown when sql is null.</exception>
        protected BlackBoxRecoveryOutcomeMethodsBase(BlackBoxSqlDialect sql)
        {
            _Sql = sql ?? throw new ArgumentNullException(nameof(sql));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<BlackBoxRecoveryOutcome> CreateAsync(BlackBoxRecoveryOutcome outcome, CancellationToken token = default)
        {
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                await _Sql.ExecuteAsync(conn, tx,
                    "INSERT INTO blackbox_recovery_outcomes (id, tenant_id, user_id, entry_id, mission_id, voyage_id, vessel_id, rule_id, path_id, runbook_id, run_succeeded, outcome, outcome_status, created_utc, resolved_utc) "
                    + "VALUES (@id, @tenant_id, @user_id, @entry_id, @mission_id, @voyage_id, @vessel_id, @rule_id, @path_id, @runbook_id, @run_succeeded, @outcome, @outcome_status, @created_utc, @resolved_utc);",
                    cmd =>
                    {
                        _Sql.Add(cmd, "@id", outcome.Id);
                        _Sql.Add(cmd, "@tenant_id", outcome.TenantId);
                        _Sql.Add(cmd, "@user_id", outcome.UserId);
                        _Sql.Add(cmd, "@entry_id", outcome.EntryId);
                        _Sql.Add(cmd, "@mission_id", outcome.MissionId);
                        _Sql.Add(cmd, "@voyage_id", outcome.VoyageId);
                        _Sql.Add(cmd, "@vessel_id", outcome.VesselId);
                        _Sql.Add(cmd, "@rule_id", outcome.RuleId);
                        _Sql.Add(cmd, "@path_id", outcome.PathId);
                        _Sql.Add(cmd, "@runbook_id", outcome.RunbookId);
                        _Sql.Add(cmd, "@run_succeeded", outcome.RunSucceeded);
                        _Sql.Add(cmd, "@outcome", outcome.Outcome.ToString());
                        _Sql.Add(cmd, "@outcome_status", outcome.OutcomeStatus);
                        _Sql.AddDate(cmd, "@created_utc", outcome.CreatedUtc);
                        _Sql.AddDate(cmd, "@resolved_utc", outcome.ResolvedUtc);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return outcome;
        }

        /// <inheritdoc />
        public async Task<BlackBoxRecoveryOutcome?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<BlackBoxRecoveryOutcome> rows = await _Sql.QueryAsync("SELECT * FROM blackbox_recovery_outcomes WHERE id = @id;",
                cmd => _Sql.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public Task<EnumerationResult<BlackBoxRecoveryOutcome>> EnumerateByEntryAsync(string entryId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));
            if (query == null) throw new ArgumentNullException(nameof(query));
            return BlackBoxSqlPaging.PageAsync(_Sql, "SELECT COUNT(*) FROM blackbox_recovery_outcomes WHERE entry_id = @entry_id",
                "SELECT * FROM blackbox_recovery_outcomes WHERE entry_id = @entry_id ORDER BY created_utc DESC, id DESC",
                cmd => _Sql.Add(cmd, "@entry_id", entryId), query.PageNumber, query.PageSize, FromReader, token);
        }

        /// <inheritdoc />
        public Task<EnumerationResult<BlackBoxRecoveryOutcome>> EnumeratePendingAsync(EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            Action<DbCommand> bind = cmd => _Sql.Add(cmd, "@outcome", BlackBoxRecoveryOutcomeEnum.Pending.ToString());
            return BlackBoxSqlPaging.PageAsync(_Sql, "SELECT COUNT(*) FROM blackbox_recovery_outcomes WHERE outcome = @outcome",
                "SELECT * FROM blackbox_recovery_outcomes WHERE outcome = @outcome ORDER BY created_utc ASC, id ASC",
                bind, query.PageNumber, query.PageSize, FromReader, token);
        }

        /// <inheritdoc />
        public async Task<bool> SettleAsync(string id, BlackBoxRecoveryOutcomeEnum outcome, string? outcomeStatus, DateTime resolvedUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            int updated = 0;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                updated = await _Sql.ExecuteAsync(conn, tx,
                    "UPDATE blackbox_recovery_outcomes SET outcome = @outcome, outcome_status = @outcome_status, resolved_utc = @resolved_utc WHERE id = @id AND outcome = @pending;",
                    cmd =>
                    {
                        _Sql.Add(cmd, "@outcome", outcome.ToString());
                        _Sql.Add(cmd, "@outcome_status", outcomeStatus);
                        _Sql.AddDate(cmd, "@resolved_utc", resolvedUtc);
                        _Sql.Add(cmd, "@id", id);
                        _Sql.Add(cmd, "@pending", BlackBoxRecoveryOutcomeEnum.Pending.ToString());
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return updated > 0;
        }

        /// <inheritdoc />
        public Task<EnumerationResult<BlackBoxRecoveryStat>> EnumerateStatsAsync(string? tenantId, string? userId, string? ruleId, string? vesselId, EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            List<string> conditions = new List<string>();
            if (tenantId != null) conditions.Add("tenant_id = @tenant_id");
            if (userId != null) conditions.Add("user_id = @user_id");
            if (ruleId != null) conditions.Add("rule_id = @rule_id");
            if (vesselId != null) conditions.Add("vessel_id = @vessel_id");
            string where = conditions.Count == 0 ? String.Empty : " WHERE " + String.Join(" AND ", conditions);
            Action<DbCommand> bind = cmd =>
            {
                if (tenantId != null) _Sql.Add(cmd, "@tenant_id", tenantId);
                if (userId != null) _Sql.Add(cmd, "@user_id", userId);
                if (ruleId != null) _Sql.Add(cmd, "@rule_id", ruleId);
                if (vesselId != null) _Sql.Add(cmd, "@vessel_id", vesselId);
            };
            string group = " GROUP BY COALESCE(rule_id, ''), path_id";
            string select = "SELECT COALESCE(rule_id, '') AS rule_key, path_id AS path_id, COUNT(*) AS runs, "
                + "SUM(CASE WHEN outcome = 'Succeeded' THEN 1 ELSE 0 END) AS succeeded, "
                + "SUM(CASE WHEN outcome = 'Failed' OR outcome = 'RunFailed' THEN 1 ELSE 0 END) AS failed, "
                + "SUM(CASE WHEN outcome = 'Pending' THEN 1 ELSE 0 END) AS pending, "
                + "SUM(CASE WHEN outcome = 'Expired' THEN 1 ELSE 0 END) AS expired, "
                + "MAX(created_utc) AS last_run FROM blackbox_recovery_outcomes" + where + group
                + " ORDER BY COUNT(*) DESC, path_id ASC, COALESCE(rule_id, '') ASC";
            return BlackBoxSqlPaging.PageAsync(_Sql, "SELECT COUNT(*) FROM (SELECT path_id FROM blackbox_recovery_outcomes" + where + group + ") t",
                select, bind, query.PageNumber, query.PageSize, r =>
                {
                    BlackBoxRecoveryStat stat = new BlackBoxRecoveryStat();
                    stat.RuleId = BlackBoxSqlDialect.ReadString(r["rule_key"]);
                    stat.PathId = BlackBoxSqlDialect.ReadString(r["path_id"]) ?? String.Empty;
                    stat.Runs = BlackBoxSqlDialect.ReadLong(r["runs"], 0);
                    stat.Succeeded = BlackBoxSqlDialect.ReadLong(r["succeeded"], 0);
                    stat.Failed = BlackBoxSqlDialect.ReadLong(r["failed"], 0);
                    stat.Pending = BlackBoxSqlDialect.ReadLong(r["pending"], 0);
                    stat.Expired = BlackBoxSqlDialect.ReadLong(r["expired"], 0);
                    long settled = stat.Succeeded + stat.Failed;
                    stat.SuccessRate = settled > 0 ? Math.Round((double)stat.Succeeded / settled, 4) : (double?)null;
                    stat.LastRunUtc = _Sql.ReadDate(r["last_run"]);
                    return stat;
                }, token);
        }

        /// <inheritdoc />
        public async Task<int> DeleteByEntryAsync(string entryId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(entryId)) throw new ArgumentNullException(nameof(entryId));
            int deleted = 0;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                deleted = await _Sql.ExecuteAsync(conn, tx, "DELETE FROM blackbox_recovery_outcomes WHERE entry_id = @entry_id;", cmd => _Sql.Add(cmd, "@entry_id", entryId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        /// <inheritdoc />
        public async Task<int> DeleteCreatedBeforeAsync(DateTime beforeUtc, CancellationToken token = default)
        {
            int deleted = 0;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                deleted = await _Sql.ExecuteAsync(conn, tx, "DELETE FROM blackbox_recovery_outcomes WHERE created_utc < @before;", cmd => _Sql.AddDate(cmd, "@before", beforeUtc), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private BlackBoxRecoveryOutcome FromReader(DbDataReader r)
        {
            BlackBoxRecoveryOutcome o = new BlackBoxRecoveryOutcome();
            o.Id = r["id"].ToString()!;
            o.TenantId = BlackBoxSqlDialect.ReadString(r["tenant_id"]);
            o.UserId = BlackBoxSqlDialect.ReadString(r["user_id"]);
            o.EntryId = BlackBoxSqlDialect.ReadString(r["entry_id"]) ?? String.Empty;
            o.MissionId = BlackBoxSqlDialect.ReadString(r["mission_id"]);
            o.VoyageId = BlackBoxSqlDialect.ReadString(r["voyage_id"]);
            o.VesselId = BlackBoxSqlDialect.ReadString(r["vessel_id"]);
            o.RuleId = BlackBoxSqlDialect.ReadString(r["rule_id"]);
            o.PathId = BlackBoxSqlDialect.ReadString(r["path_id"]) ?? String.Empty;
            o.RunbookId = BlackBoxSqlDialect.ReadString(r["runbook_id"]);
            o.RunSucceeded = BlackBoxSqlDialect.ReadBool(r["run_succeeded"], false);
            o.Outcome = BlackBoxSqlDialect.ReadEnum(r["outcome"], BlackBoxRecoveryOutcomeEnum.Unknown);
            o.OutcomeStatus = BlackBoxSqlDialect.ReadString(r["outcome_status"]);
            o.CreatedUtc = _Sql.ReadDate(r["created_utc"]) ?? DateTime.UtcNow;
            o.ResolvedUtc = _Sql.ReadDate(r["resolved_utc"]);
            return o;
        }

        #endregion
    }
}
