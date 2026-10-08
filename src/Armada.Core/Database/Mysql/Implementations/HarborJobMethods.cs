namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of Harbor job record persistence.
    /// </summary>
    public class HarborJobMethods : IHarborJobMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO harbor_jobs
            (id, job_id, harbor_id, tenant_id, kind, runtime, model, mission_id, captain_id, launched_utc, started_utc, first_output_utc, ended_utc,
             time_to_first_output_ms, duration_ms, exit_code, outcome, stop_requested, created_utc, last_update_utc)
            VALUES
            (@id, @job_id, @harbor_id, @tenant_id, @kind, @runtime, @model, @mission_id, @captain_id, @launched_utc, @started_utc, @first_output_utc, @ended_utc,
             @time_to_first_output_ms, @duration_ms, @exit_code, @outcome, @stop_requested, @created_utc, @last_update_utc);";

        private static readonly string _Update = @"UPDATE harbor_jobs SET
            job_id = @job_id, harbor_id = @harbor_id, tenant_id = @tenant_id, kind = @kind, runtime = @runtime, model = @model,
            mission_id = @mission_id, captain_id = @captain_id, launched_utc = @launched_utc, started_utc = @started_utc,
            first_output_utc = @first_output_utc, ended_utc = @ended_utc, time_to_first_output_ms = @time_to_first_output_ms,
            duration_ms = @duration_ms, exit_code = @exit_code, outcome = @outcome, stop_requested = @stop_requested,
            last_update_utc = @last_update_utc
            WHERE id = @id;";

        private readonly string _ConnectionString;
        private readonly SemaphoreSlim? _WriteLock;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when connectionString is null.</exception>
        public HarborJobMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _WriteLock = null;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<HarborJobRecord> CreateAsync(HarborJobRecord record, CancellationToken token = default)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (String.IsNullOrEmpty(record.JobId)) throw new ArgumentException("JobId is required.", nameof(record));
            if (String.IsNullOrEmpty(record.HarborId)) throw new ArgumentException("HarborId is required.", nameof(record));
            record.LastUpdateUtc = DateTime.UtcNow;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, record), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return record;
        }

        /// <inheritdoc />
        public async Task<HarborJobRecord> UpdateAsync(HarborJobRecord record, CancellationToken token = default)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            record.LastUpdateUtc = DateTime.UtcNow;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Update, cmd => Bind(cmd, record), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return record;
        }

        /// <inheritdoc />
        public async Task<HarborJobRecord?> ReadByJobIdAsync(string jobId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(jobId)) throw new ArgumentNullException(nameof(jobId));
            List<HarborJobRecord> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM harbor_jobs WHERE job_id = @job_id ORDER BY created_utc DESC" + MysqlCommandHelper.Page(0, 1) + ";",
                cmd => MysqlCommandHelper.Add(cmd, "@job_id", jobId), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<List<HarborJobRecord>> EnumerateAsync(string harborId, DateTime fromUtc, DateTime toUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            return await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM harbor_jobs WHERE harbor_id = @harbor_id AND launched_utc < @to_utc AND (ended_utc IS NULL OR ended_utc >= @from_utc) ORDER BY launched_utc ASC, id ASC;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@harbor_id", harborId);
                    MysqlCommandHelper.AddDate(cmd, "@from_utc", fromUtc);
                    MysqlCommandHelper.AddDate(cmd, "@to_utc", toUtc);
                }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<HarborJobRecord>> EnumerateOpenAsync(string harborId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            return await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM harbor_jobs WHERE harbor_id = @harbor_id AND ended_utc IS NULL ORDER BY launched_utc ASC, id ASC;",
                cmd => MysqlCommandHelper.Add(cmd, "@harbor_id", harborId), FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> DeleteEndedBeforeAsync(DateTime cutoffUtc, CancellationToken token = default)
        {
            int deleted = 0;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                deleted = await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM harbor_jobs WHERE ended_utc IS NOT NULL AND ended_utc < @cutoff;",
                    cmd => MysqlCommandHelper.AddDate(cmd, "@cutoff", cutoffUtc), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        /// <inheritdoc />
        public async Task<int> DeleteByHarborAsync(string harborId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            int deleted = 0;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                deleted = await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM harbor_jobs WHERE harbor_id = @harbor_id;",
                    cmd => MysqlCommandHelper.Add(cmd, "@harbor_id", harborId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static void Bind(MySqlCommand cmd, HarborJobRecord record)
        {
            MysqlCommandHelper.Add(cmd, "@id", record.Id);
            MysqlCommandHelper.Add(cmd, "@job_id", record.JobId);
            MysqlCommandHelper.Add(cmd, "@harbor_id", record.HarborId);
            MysqlCommandHelper.Add(cmd, "@tenant_id", record.TenantId);
            MysqlCommandHelper.Add(cmd, "@kind", record.Kind.ToString());
            MysqlCommandHelper.Add(cmd, "@runtime", record.Runtime ?? String.Empty);
            MysqlCommandHelper.Add(cmd, "@model", record.Model);
            MysqlCommandHelper.Add(cmd, "@mission_id", record.MissionId);
            MysqlCommandHelper.Add(cmd, "@captain_id", record.CaptainId);
            MysqlCommandHelper.AddDate(cmd, "@launched_utc", record.LaunchedUtc);
            MysqlCommandHelper.AddDate(cmd, "@started_utc", record.StartedUtc);
            MysqlCommandHelper.AddDate(cmd, "@first_output_utc", record.FirstOutputUtc);
            MysqlCommandHelper.AddDate(cmd, "@ended_utc", record.EndedUtc);
            MysqlCommandHelper.Add(cmd, "@time_to_first_output_ms", record.TimeToFirstOutputMs);
            MysqlCommandHelper.Add(cmd, "@duration_ms", record.DurationMs);
            MysqlCommandHelper.Add(cmd, "@exit_code", record.ExitCode);
            MysqlCommandHelper.Add(cmd, "@outcome", record.Outcome.ToString());
            MysqlCommandHelper.Add(cmd, "@stop_requested", record.StopRequested);
            MysqlCommandHelper.AddDate(cmd, "@created_utc", record.CreatedUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", record.LastUpdateUtc);
        }

        private static HarborJobRecord FromReader(MySqlDataReader reader)
        {
            HarborJobRecord record = new HarborJobRecord();
            record.Id = reader["id"].ToString()!;
            record.JobId = reader["job_id"]?.ToString() ?? String.Empty;
            record.HarborId = reader["harbor_id"]?.ToString() ?? String.Empty;
            record.TenantId = MysqlCommandHelper.ReadString(reader["tenant_id"]);
            record.Kind = MysqlCommandHelper.ReadEnum(reader["kind"], HarborJobKindEnum.Unknown);
            record.Runtime = MysqlCommandHelper.ReadString(reader["runtime"]) ?? String.Empty;
            record.Model = MysqlCommandHelper.ReadString(reader["model"]);
            record.MissionId = MysqlCommandHelper.ReadString(reader["mission_id"]);
            record.CaptainId = MysqlCommandHelper.ReadString(reader["captain_id"]);
            record.LaunchedUtc = MysqlCommandHelper.ReadDate(reader["launched_utc"]);
            record.StartedUtc = MysqlCommandHelper.ReadNullableDate(reader["started_utc"]);
            record.FirstOutputUtc = MysqlCommandHelper.ReadNullableDate(reader["first_output_utc"]);
            record.EndedUtc = MysqlCommandHelper.ReadNullableDate(reader["ended_utc"]);
            record.TimeToFirstOutputMs = MysqlCommandHelper.ReadNullableLong(reader["time_to_first_output_ms"]);
            record.DurationMs = MysqlCommandHelper.ReadNullableLong(reader["duration_ms"]);
            record.ExitCode = MysqlCommandHelper.ReadNullableInt(reader["exit_code"]);
            record.Outcome = MysqlCommandHelper.ReadEnum(reader["outcome"], HarborJobOutcomeEnum.Running);
            record.StopRequested = MysqlCommandHelper.ReadBool(reader["stop_requested"], false);
            record.CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]);
            record.LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return record;
        }

        #endregion
    }
}
