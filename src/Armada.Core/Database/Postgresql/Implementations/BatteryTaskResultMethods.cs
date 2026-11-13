namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// PostgreSQL implementation of battery task result persistence.
    /// </summary>
    public class BatteryTaskResultMethods : IBatteryTaskResultMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO battery_task_results
            (id, run_id, tenant_id, pack_id, pack_version, task_id, task_title, persona, grader, attempt, outcome, score,
             summary, reason, details_json, output, vessel_id, voyage_id, mission_id, mission_status, fixture_commit,
             duration_ms, created_utc, completed_utc)
            VALUES
            (@id, @run_id, @tenant_id, @pack_id, @pack_version, @task_id, @task_title, @persona, @grader, @attempt, @outcome, @score,
             @summary, @reason, @details_json, @output, @vessel_id, @voyage_id, @mission_id, @mission_status, @fixture_commit,
             @duration_ms, @created_utc, @completed_utc);";

        private static readonly JsonSerializerOptions _Json = CreateJsonOptions();

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
        public BatteryTaskResultMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<BatteryTaskResult> CreateAsync(BatteryTaskResult result, CancellationToken token = default)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, result), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return result;
        }

        /// <inheritdoc />
        public async Task<List<BatteryTaskResult>> ReadByRunAsync(string runId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(runId)) throw new ArgumentNullException(nameof(runId));
            return await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM battery_task_results WHERE run_id = @run_id ORDER BY created_utc ASC, id ASC;",
                cmd => PostgresqlCommandHelper.Add(cmd, "@run_id", runId), FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<BatteryTaskResult>> EnumerateByRunAsync(string runId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(runId)) throw new ArgumentNullException(nameof(runId));
            if (query == null) throw new ArgumentNullException(nameof(query));
            int pageSize = query.PageSize < 1 ? 1 : (query.PageSize > 1000 ? 1000 : query.PageSize);
            int pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
            int offset = (pageNumber - 1) * pageSize;

            string where = " WHERE run_id = @run_id" + (String.IsNullOrWhiteSpace(query.Status) ? String.Empty : " AND outcome = @outcome");
            Action<NpgsqlCommand> bind = cmd =>
            {
                PostgresqlCommandHelper.Add(cmd, "@run_id", runId);
                if (!String.IsNullOrWhiteSpace(query.Status)) PostgresqlCommandHelper.Add(cmd, "@outcome", query.Status);
            };
            string direction = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

            using (NpgsqlConnection conn = new NpgsqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await PostgresqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM battery_task_results" + where + ";", bind, token).ConfigureAwait(false);
                List<BatteryTaskResult> rows = await PostgresqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM battery_task_results" + where + " ORDER BY created_utc " + direction + ", id " + direction + PostgresqlCommandHelper.Page(offset, pageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);

                EnumerationResult<BatteryTaskResult> result = new EnumerationResult<BatteryTaskResult>();
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalRecords = total;
                result.TotalPages = (int)Math.Ceiling((double)total / pageSize);
                result.Objects = rows;
                return result;
            }
        }

        /// <inheritdoc />
        public async Task<int> DeleteByRunAsync(string runId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(runId)) throw new ArgumentNullException(nameof(runId));
            int deleted = 0;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                deleted = await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM battery_task_results WHERE run_id = @run_id;",
                    cmd => PostgresqlCommandHelper.Add(cmd, "@run_id", runId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static JsonSerializerOptions CreateJsonOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.PropertyNameCaseInsensitive = true;
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        private static string SerializeDetails(BatteryGradeDetails? details)
        {
            return JsonSerializer.Serialize(details ?? new BatteryGradeDetails(), _Json);
        }

        private static BatteryGradeDetails DeserializeDetails(object value)
        {
            string? json = PostgresqlCommandHelper.ReadString(value);
            if (String.IsNullOrEmpty(json)) return new BatteryGradeDetails();
            try
            {
                return JsonSerializer.Deserialize<BatteryGradeDetails>(json, _Json) ?? new BatteryGradeDetails();
            }
            catch (JsonException)
            {
                return new BatteryGradeDetails();
            }
        }

        private static void Bind(NpgsqlCommand cmd, BatteryTaskResult result)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", result.Id);
            PostgresqlCommandHelper.Add(cmd, "@run_id", result.RunId ?? String.Empty);
            PostgresqlCommandHelper.Add(cmd, "@tenant_id", result.TenantId);
            PostgresqlCommandHelper.Add(cmd, "@pack_id", result.PackId ?? String.Empty);
            PostgresqlCommandHelper.Add(cmd, "@pack_version", result.PackVersion);
            PostgresqlCommandHelper.Add(cmd, "@task_id", result.TaskId ?? String.Empty);
            PostgresqlCommandHelper.Add(cmd, "@task_title", result.TaskTitle);
            PostgresqlCommandHelper.Add(cmd, "@persona", result.Persona);
            PostgresqlCommandHelper.Add(cmd, "@grader", result.Grader.ToString());
            PostgresqlCommandHelper.Add(cmd, "@attempt", result.Attempt);
            PostgresqlCommandHelper.Add(cmd, "@outcome", result.Outcome.ToString());
            PostgresqlCommandHelper.Add(cmd, "@score", result.Score);
            PostgresqlCommandHelper.Add(cmd, "@summary", result.Summary);
            PostgresqlCommandHelper.Add(cmd, "@reason", result.Reason);
            PostgresqlCommandHelper.Add(cmd, "@details_json", SerializeDetails(result.Details));
            PostgresqlCommandHelper.Add(cmd, "@output", result.Output);
            PostgresqlCommandHelper.Add(cmd, "@vessel_id", result.VesselId);
            PostgresqlCommandHelper.Add(cmd, "@voyage_id", result.VoyageId);
            PostgresqlCommandHelper.Add(cmd, "@mission_id", result.MissionId);
            PostgresqlCommandHelper.Add(cmd, "@mission_status", result.MissionStatus);
            PostgresqlCommandHelper.Add(cmd, "@fixture_commit", result.FixtureCommit);
            PostgresqlCommandHelper.Add(cmd, "@duration_ms", result.DurationMs);
            PostgresqlCommandHelper.AddDate(cmd, "@created_utc", result.CreatedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@completed_utc", result.CompletedUtc);
        }

        private static BatteryTaskResult FromReader(NpgsqlDataReader reader)
        {
            BatteryTaskResult result = new BatteryTaskResult();
            result.Id = reader["id"].ToString()!;
            result.RunId = PostgresqlCommandHelper.ReadString(reader["run_id"]) ?? String.Empty;
            result.TenantId = PostgresqlCommandHelper.ReadString(reader["tenant_id"]);
            result.PackId = PostgresqlCommandHelper.ReadString(reader["pack_id"]) ?? String.Empty;
            result.PackVersion = PostgresqlCommandHelper.ReadInt(reader["pack_version"], 0);
            result.TaskId = PostgresqlCommandHelper.ReadString(reader["task_id"]) ?? String.Empty;
            result.TaskTitle = PostgresqlCommandHelper.ReadString(reader["task_title"]);
            result.Persona = PostgresqlCommandHelper.ReadString(reader["persona"]);
            result.Grader = PostgresqlCommandHelper.ReadEnum(reader["grader"], BatteryGraderTypeEnum.Command);
            result.Attempt = PostgresqlCommandHelper.ReadInt(reader["attempt"], 1);
            result.Outcome = PostgresqlCommandHelper.ReadEnum(reader["outcome"], BatteryTaskOutcomeEnum.Failed);
            result.Score = PostgresqlCommandHelper.ReadNullableDouble(reader["score"]);
            result.Summary = PostgresqlCommandHelper.ReadString(reader["summary"]);
            result.Reason = PostgresqlCommandHelper.ReadString(reader["reason"]);
            result.Details = DeserializeDetails(reader["details_json"]);
            result.Output = PostgresqlCommandHelper.ReadString(reader["output"]);
            result.VesselId = PostgresqlCommandHelper.ReadString(reader["vessel_id"]);
            result.VoyageId = PostgresqlCommandHelper.ReadString(reader["voyage_id"]);
            result.MissionId = PostgresqlCommandHelper.ReadString(reader["mission_id"]);
            result.MissionStatus = PostgresqlCommandHelper.ReadString(reader["mission_status"]);
            result.FixtureCommit = PostgresqlCommandHelper.ReadString(reader["fixture_commit"]);
            result.DurationMs = PostgresqlCommandHelper.ReadNullableLong(reader["duration_ms"]) ?? 0;
            result.CreatedUtc = PostgresqlCommandHelper.ReadDate(reader["created_utc"]);
            result.CompletedUtc = PostgresqlCommandHelper.ReadNullableDate(reader["completed_utc"]);
            return result;
        }

        #endregion
    }
}
