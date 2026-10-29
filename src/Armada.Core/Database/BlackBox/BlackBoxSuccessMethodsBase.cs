namespace Armada.Core.Database.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;

    /// <summary>
    /// Success flight recorder persistence shared by the four providers. Thread-safe.
    /// </summary>
    public abstract class BlackBoxSuccessMethodsBase : IBlackBoxSuccessMethods
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
        protected BlackBoxSuccessMethodsBase(BlackBoxSqlDialect sql)
        {
            _Sql = sql ?? throw new ArgumentNullException(nameof(sql));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<BlackBoxSuccess> CreateAsync(BlackBoxSuccess success, CancellationToken token = default)
        {
            if (success == null) throw new ArgumentNullException(nameof(success));
            List<string> files = success.FilesChanged.Take(BlackBoxInsightLimits.MaxSuccessFiles).ToList();
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                await _Sql.ExecuteAsync(conn, tx,
                    "INSERT INTO blackbox_successes (id, tenant_id, user_id, vessel_id, mission_id, voyage_id, captain_id, mission_title, base_commit, target_commit, target_branch, files_changed, file_count, mission_created_utc, started_utc, landed_utc, queue_ms, run_ms, total_ms, created_utc, expires_utc) "
                    + "VALUES (@id, @tenant_id, @user_id, @vessel_id, @mission_id, @voyage_id, @captain_id, @mission_title, @base_commit, @target_commit, @target_branch, @files_changed, @file_count, @mission_created_utc, @started_utc, @landed_utc, @queue_ms, @run_ms, @total_ms, @created_utc, @expires_utc);",
                    cmd =>
                    {
                        _Sql.Add(cmd, "@id", success.Id);
                        _Sql.Add(cmd, "@tenant_id", success.TenantId);
                        _Sql.Add(cmd, "@user_id", success.UserId);
                        _Sql.Add(cmd, "@vessel_id", success.VesselId);
                        _Sql.Add(cmd, "@mission_id", success.MissionId);
                        _Sql.Add(cmd, "@voyage_id", success.VoyageId);
                        _Sql.Add(cmd, "@captain_id", success.CaptainId);
                        _Sql.Add(cmd, "@mission_title", BlackBoxLimits.Clip(success.MissionTitle, BlackBoxLimits.MaxTitleChars));
                        _Sql.Add(cmd, "@base_commit", BlackBoxLimits.Clip(success.BaseCommit, 64));
                        _Sql.Add(cmd, "@target_commit", BlackBoxLimits.Clip(success.TargetCommit, 64));
                        _Sql.Add(cmd, "@target_branch", BlackBoxLimits.Clip(success.TargetBranch, 256));
                        _Sql.Add(cmd, "@files_changed", files.Count == 0 ? null : String.Join("\n", files));
                        _Sql.Add(cmd, "@file_count", Math.Max(success.FileCount, files.Count));
                        _Sql.AddDate(cmd, "@mission_created_utc", success.MissionCreatedUtc);
                        _Sql.AddDate(cmd, "@started_utc", success.StartedUtc);
                        _Sql.AddDate(cmd, "@landed_utc", success.LandedUtc);
                        _Sql.Add(cmd, "@queue_ms", success.QueueMs);
                        _Sql.Add(cmd, "@run_ms", success.RunMs);
                        _Sql.Add(cmd, "@total_ms", success.TotalMs);
                        _Sql.AddDate(cmd, "@created_utc", success.CreatedUtc);
                        _Sql.AddDate(cmd, "@expires_utc", success.ExpiresUtc);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return success;
        }

        /// <inheritdoc />
        public async Task<BlackBoxSuccess?> ReadAsync(string id, string? tenantId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<BlackBoxSuccess> rows = await _Sql.QueryAsync("SELECT * FROM blackbox_successes WHERE id = @id" + (tenantId != null ? " AND tenant_id = @tenant_id" : String.Empty) + ";",
                cmd =>
                {
                    _Sql.Add(cmd, "@id", id);
                    if (tenantId != null) _Sql.Add(cmd, "@tenant_id", tenantId);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<BlackBoxSuccess?> ReadLatestByMissionAsync(string missionId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            List<BlackBoxSuccess> rows = await _Sql.QueryAsync("SELECT * FROM blackbox_successes WHERE mission_id = @mission_id ORDER BY landed_utc DESC, id DESC" + _Sql.Page(0, 1) + ";",
                cmd => _Sql.Add(cmd, "@mission_id", missionId), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public Task<EnumerationResult<BlackBoxSuccess>> EnumerateByVesselAsync(string vesselId, string? tenantId, string? userId, DateTime fromUtc, DateTime toUtc, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            if (query == null) throw new ArgumentNullException(nameof(query));
            string where = " WHERE vessel_id = @vessel_id AND landed_utc >= @from_utc AND landed_utc <= @to_utc"
                + (tenantId != null ? " AND tenant_id = @tenant_id" : String.Empty)
                + (userId != null ? " AND user_id = @user_id" : String.Empty);
            Action<DbCommand> bind = cmd =>
            {
                _Sql.Add(cmd, "@vessel_id", vesselId);
                _Sql.AddDate(cmd, "@from_utc", fromUtc);
                _Sql.AddDate(cmd, "@to_utc", toUtc);
                if (tenantId != null) _Sql.Add(cmd, "@tenant_id", tenantId);
                if (userId != null) _Sql.Add(cmd, "@user_id", userId);
            };
            return BlackBoxSqlPaging.PageAsync(_Sql, "SELECT COUNT(*) FROM blackbox_successes" + where,
                "SELECT * FROM blackbox_successes" + where + " ORDER BY landed_utc DESC, id DESC",
                bind, query.PageNumber, query.PageSize, FromReader, token);
        }

        /// <inheritdoc />
        public Task<long> CountAsync(CancellationToken token = default)
        {
            return _Sql.ScalarLongAsync("SELECT COUNT(*) FROM blackbox_successes;", null, token);
        }

        /// <inheritdoc />
        public async Task<int> DeleteExpiredAsync(DateTime beforeUtc, CancellationToken token = default)
        {
            int deleted = 0;
            await _Sql.WriteAsync(async (DbConnection conn, DbTransaction tx) =>
            {
                deleted = await _Sql.ExecuteAsync(conn, tx, "DELETE FROM blackbox_successes WHERE expires_utc IS NULL OR expires_utc < @before;", cmd => _Sql.AddDate(cmd, "@before", beforeUtc), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private BlackBoxSuccess FromReader(DbDataReader r)
        {
            BlackBoxSuccess s = new BlackBoxSuccess();
            s.Id = r["id"].ToString()!;
            s.TenantId = BlackBoxSqlDialect.ReadString(r["tenant_id"]);
            s.UserId = BlackBoxSqlDialect.ReadString(r["user_id"]);
            s.VesselId = BlackBoxSqlDialect.ReadString(r["vessel_id"]);
            s.MissionId = BlackBoxSqlDialect.ReadString(r["mission_id"]) ?? String.Empty;
            s.VoyageId = BlackBoxSqlDialect.ReadString(r["voyage_id"]);
            s.CaptainId = BlackBoxSqlDialect.ReadString(r["captain_id"]);
            s.MissionTitle = BlackBoxSqlDialect.ReadString(r["mission_title"]);
            s.BaseCommit = BlackBoxSqlDialect.ReadString(r["base_commit"]);
            s.TargetCommit = BlackBoxSqlDialect.ReadString(r["target_commit"]);
            s.TargetBranch = BlackBoxSqlDialect.ReadString(r["target_branch"]);
            string? files = BlackBoxSqlDialect.ReadString(r["files_changed"]);
            s.FilesChanged = files == null ? new List<string>() : files.Split('\n').Where(f => f.Length > 0).Take(BlackBoxInsightLimits.MaxSuccessFiles).ToList();
            s.FileCount = (int)BlackBoxSqlDialect.ReadLong(r["file_count"], 0);
            s.MissionCreatedUtc = _Sql.ReadDate(r["mission_created_utc"]);
            s.StartedUtc = _Sql.ReadDate(r["started_utc"]);
            s.LandedUtc = _Sql.ReadDate(r["landed_utc"]) ?? DateTime.UtcNow;
            s.QueueMs = r["queue_ms"] == DBNull.Value ? null : BlackBoxSqlDialect.ReadLong(r["queue_ms"], 0);
            s.RunMs = r["run_ms"] == DBNull.Value ? null : BlackBoxSqlDialect.ReadLong(r["run_ms"], 0);
            s.TotalMs = r["total_ms"] == DBNull.Value ? null : BlackBoxSqlDialect.ReadLong(r["total_ms"], 0);
            s.CreatedUtc = _Sql.ReadDate(r["created_utc"]) ?? DateTime.UtcNow;
            s.ExpiresUtc = _Sql.ReadDate(r["expires_utc"]);
            return s;
        }

        #endregion
    }
}
