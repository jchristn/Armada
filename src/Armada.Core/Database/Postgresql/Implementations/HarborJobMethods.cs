namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// PostgreSQL implementation of Harbor job record persistence.
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
        /// <param name="driver">PostgreSQL database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public HarborJobMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _ConnectionString = driver.ConnectionString;
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
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, record), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return record;
        }

        /// <inheritdoc />
        public async Task<HarborJobRecord> UpdateAsync(HarborJobRecord record, CancellationToken token = default)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            record.LastUpdateUtc = DateTime.UtcNow;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Update, cmd => Bind(cmd, record), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return record;
        }

        /// <inheritdoc />
        public async Task<HarborJobRecord?> ReadByJobIdAsync(string jobId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(jobId)) throw new ArgumentNullException(nameof(jobId));
            List<HarborJobRecord> rows = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM harbor_jobs WHERE job_id = @job_id ORDER BY created_utc DESC" + PostgresqlCommandHelper.Page(0, 1) + ";",
                cmd => PostgresqlCommandHelper.Add(cmd, "@job_id", jobId), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<List<HarborJobRecord>> EnumerateAsync(string harborId, DateTime fromUtc, DateTime toUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            return await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM harbor_jobs WHERE harbor_id = @harbor_id AND launched_utc < @to_utc AND (ended_utc IS NULL OR ended_utc >= @from_utc) ORDER BY launched_utc ASC, id ASC;",
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@harbor_id", harborId);
                    PostgresqlCommandHelper.AddDate(cmd, "@from_utc", fromUtc);
                    PostgresqlCommandHelper.AddDate(cmd, "@to_utc", toUtc);
                }, FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<HarborJobRecord>> EnumerateOpenAsync(string harborId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            return await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM harbor_jobs WHERE harbor_id = @harbor_id AND ended_utc IS NULL ORDER BY launched_utc ASC, id ASC;",
                cmd => PostgresqlCommandHelper.Add(cmd, "@harbor_id", harborId), FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> DeleteEndedBeforeAsync(DateTime cutoffUtc, CancellationToken token = default)
        {
            int deleted = 0;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                deleted = await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM harbor_jobs WHERE ended_utc IS NOT NULL AND ended_utc < @cutoff;",
                    cmd => PostgresqlCommandHelper.AddDate(cmd, "@cutoff", cutoffUtc), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        /// <inheritdoc />
        public async Task<int> DeleteByHarborAsync(string harborId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(harborId)) throw new ArgumentNullException(nameof(harborId));
            int deleted = 0;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                deleted = await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM harbor_jobs WHERE harbor_id = @harbor_id;",
                    cmd => PostgresqlCommandHelper.Add(cmd, "@harbor_id", harborId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static void Bind(NpgsqlCommand cmd, HarborJobRecord record)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", record.Id);
            PostgresqlCommandHelper.Add(cmd, "@job_id", record.JobId);
            PostgresqlCommandHelper.Add(cmd, "@harbor_id", record.HarborId);
            PostgresqlCommandHelper.Add(cmd, "@tenant_id", record.TenantId);
            PostgresqlCommandHelper.Add(cmd, "@kind", record.Kind.ToString());
            PostgresqlCommandHelper.Add(cmd, "@runtime", record.Runtime ?? String.Empty);
            PostgresqlCommandHelper.Add(cmd, "@model", record.Model);
            PostgresqlCommandHelper.Add(cmd, "@mission_id", record.MissionId);
            PostgresqlCommandHelper.Add(cmd, "@captain_id", record.CaptainId);
            PostgresqlCommandHelper.AddDate(cmd, "@launched_utc", record.LaunchedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@started_utc", record.StartedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@first_output_utc", record.FirstOutputUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@ended_utc", record.EndedUtc);
            PostgresqlCommandHelper.Add(cmd, "@time_to_first_output_ms", record.TimeToFirstOutputMs);
            PostgresqlCommandHelper.Add(cmd, "@duration_ms", record.DurationMs);
            PostgresqlCommandHelper.Add(cmd, "@exit_code", record.ExitCode);
            PostgresqlCommandHelper.Add(cmd, "@outcome", record.Outcome.ToString());
            PostgresqlCommandHelper.Add(cmd, "@stop_requested", record.StopRequested);
            PostgresqlCommandHelper.AddDate(cmd, "@created_utc", record.CreatedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", record.LastUpdateUtc);
        }

        private static HarborJobRecord FromReader(NpgsqlDataReader reader)
        {
            HarborJobRecord record = new HarborJobRecord();
            record.Id = reader["id"].ToString()!;
            record.JobId = reader["job_id"]?.ToString() ?? String.Empty;
            record.HarborId = reader["harbor_id"]?.ToString() ?? String.Empty;
            record.TenantId = PostgresqlCommandHelper.ReadString(reader["tenant_id"]);
            record.Kind = PostgresqlCommandHelper.ReadEnum(reader["kind"], HarborJobKindEnum.Unknown);
            record.Runtime = PostgresqlCommandHelper.ReadString(reader["runtime"]) ?? String.Empty;
            record.Model = PostgresqlCommandHelper.ReadString(reader["model"]);
            record.MissionId = PostgresqlCommandHelper.ReadString(reader["mission_id"]);
            record.CaptainId = PostgresqlCommandHelper.ReadString(reader["captain_id"]);
            record.LaunchedUtc = PostgresqlCommandHelper.ReadDate(reader["launched_utc"]);
            record.StartedUtc = PostgresqlCommandHelper.ReadNullableDate(reader["started_utc"]);
            record.FirstOutputUtc = PostgresqlCommandHelper.ReadNullableDate(reader["first_output_utc"]);
            record.EndedUtc = PostgresqlCommandHelper.ReadNullableDate(reader["ended_utc"]);
            record.TimeToFirstOutputMs = PostgresqlCommandHelper.ReadNullableLong(reader["time_to_first_output_ms"]);
            record.DurationMs = PostgresqlCommandHelper.ReadNullableLong(reader["duration_ms"]);
            record.ExitCode = PostgresqlCommandHelper.ReadNullableInt(reader["exit_code"]);
            record.Outcome = PostgresqlCommandHelper.ReadEnum(reader["outcome"], HarborJobOutcomeEnum.Running);
            record.StopRequested = PostgresqlCommandHelper.ReadBool(reader["stop_requested"], false);
            record.CreatedUtc = PostgresqlCommandHelper.ReadDate(reader["created_utc"]);
            record.LastUpdateUtc = PostgresqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return record;
        }

        #endregion
    }
}
