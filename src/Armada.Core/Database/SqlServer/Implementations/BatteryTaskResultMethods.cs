namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.SqlClient;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQL Server implementation of battery task result persistence.
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
        /// <param name="driver">SQL Server database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public BatteryTaskResultMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, result), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return result;
        }

        /// <inheritdoc />
        public async Task<List<BatteryTaskResult>> ReadByRunAsync(string runId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(runId)) throw new ArgumentNullException(nameof(runId));
            return await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM battery_task_results WHERE run_id = @run_id ORDER BY created_utc ASC, id COLLATE Latin1_General_BIN2 ASC;",
                cmd => SqlServerCommandHelper.Add(cmd, "@run_id", runId), FromReader, token).ConfigureAwait(false);
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
            Action<SqlCommand> bind = cmd =>
            {
                SqlServerCommandHelper.Add(cmd, "@run_id", runId);
                if (!String.IsNullOrWhiteSpace(query.Status)) SqlServerCommandHelper.Add(cmd, "@outcome", query.Status);
            };
            string direction = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqlServerCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM battery_task_results" + where + ";", bind, token).ConfigureAwait(false);
                List<BatteryTaskResult> rows = await SqlServerCommandHelper.QueryAsync(conn,
                    // Ordinal id tie-break: the default collation is case-insensitive, so ids that share a timestamp would sort
                    // differently from the other providers.
                    "SELECT * FROM battery_task_results" + where + " ORDER BY created_utc " + direction + ", id COLLATE Latin1_General_BIN2 " + direction + SqlServerCommandHelper.Page(offset, pageSize) + ";",
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
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                deleted = await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM battery_task_results WHERE run_id = @run_id;",
                    cmd => SqlServerCommandHelper.Add(cmd, "@run_id", runId), token).ConfigureAwait(false);
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
            string? json = SqlServerCommandHelper.ReadString(value);
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

        private static void Bind(SqlCommand cmd, BatteryTaskResult result)
        {
            SqlServerCommandHelper.Add(cmd, "@id", result.Id);
            SqlServerCommandHelper.Add(cmd, "@run_id", result.RunId ?? String.Empty);
            SqlServerCommandHelper.Add(cmd, "@tenant_id", result.TenantId);
            SqlServerCommandHelper.Add(cmd, "@pack_id", result.PackId ?? String.Empty);
            SqlServerCommandHelper.Add(cmd, "@pack_version", result.PackVersion);
            SqlServerCommandHelper.Add(cmd, "@task_id", result.TaskId ?? String.Empty);
            SqlServerCommandHelper.Add(cmd, "@task_title", result.TaskTitle);
            SqlServerCommandHelper.Add(cmd, "@persona", result.Persona);
            SqlServerCommandHelper.Add(cmd, "@grader", result.Grader.ToString());
            SqlServerCommandHelper.Add(cmd, "@attempt", result.Attempt);
            SqlServerCommandHelper.Add(cmd, "@outcome", result.Outcome.ToString());
            SqlServerCommandHelper.Add(cmd, "@score", result.Score);
            SqlServerCommandHelper.Add(cmd, "@summary", result.Summary);
            SqlServerCommandHelper.Add(cmd, "@reason", result.Reason);
            SqlServerCommandHelper.Add(cmd, "@details_json", SerializeDetails(result.Details));
            SqlServerCommandHelper.Add(cmd, "@output", result.Output);
            SqlServerCommandHelper.Add(cmd, "@vessel_id", result.VesselId);
            SqlServerCommandHelper.Add(cmd, "@voyage_id", result.VoyageId);
            SqlServerCommandHelper.Add(cmd, "@mission_id", result.MissionId);
            SqlServerCommandHelper.Add(cmd, "@mission_status", result.MissionStatus);
            SqlServerCommandHelper.Add(cmd, "@fixture_commit", result.FixtureCommit);
            SqlServerCommandHelper.Add(cmd, "@duration_ms", result.DurationMs);
            SqlServerCommandHelper.AddDate(cmd, "@created_utc", result.CreatedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@completed_utc", result.CompletedUtc);
        }

        private static BatteryTaskResult FromReader(SqlDataReader reader)
        {
            BatteryTaskResult result = new BatteryTaskResult();
            result.Id = reader["id"].ToString()!;
            result.RunId = SqlServerCommandHelper.ReadString(reader["run_id"]) ?? String.Empty;
            result.TenantId = SqlServerCommandHelper.ReadString(reader["tenant_id"]);
            result.PackId = SqlServerCommandHelper.ReadString(reader["pack_id"]) ?? String.Empty;
            result.PackVersion = SqlServerCommandHelper.ReadInt(reader["pack_version"], 0);
            result.TaskId = SqlServerCommandHelper.ReadString(reader["task_id"]) ?? String.Empty;
            result.TaskTitle = SqlServerCommandHelper.ReadString(reader["task_title"]);
            result.Persona = SqlServerCommandHelper.ReadString(reader["persona"]);
            result.Grader = SqlServerCommandHelper.ReadEnum(reader["grader"], BatteryGraderTypeEnum.Command);
            result.Attempt = SqlServerCommandHelper.ReadInt(reader["attempt"], 1);
            result.Outcome = SqlServerCommandHelper.ReadEnum(reader["outcome"], BatteryTaskOutcomeEnum.Failed);
            result.Score = SqlServerCommandHelper.ReadNullableDouble(reader["score"]);
            result.Summary = SqlServerCommandHelper.ReadString(reader["summary"]);
            result.Reason = SqlServerCommandHelper.ReadString(reader["reason"]);
            result.Details = DeserializeDetails(reader["details_json"]);
            result.Output = SqlServerCommandHelper.ReadString(reader["output"]);
            result.VesselId = SqlServerCommandHelper.ReadString(reader["vessel_id"]);
            result.VoyageId = SqlServerCommandHelper.ReadString(reader["voyage_id"]);
            result.MissionId = SqlServerCommandHelper.ReadString(reader["mission_id"]);
            result.MissionStatus = SqlServerCommandHelper.ReadString(reader["mission_status"]);
            result.FixtureCommit = SqlServerCommandHelper.ReadString(reader["fixture_commit"]);
            result.DurationMs = SqlServerCommandHelper.ReadNullableLong(reader["duration_ms"]) ?? 0;
            result.CreatedUtc = SqlServerCommandHelper.ReadDate(reader["created_utc"]);
            result.CompletedUtc = SqlServerCommandHelper.ReadNullableDate(reader["completed_utc"]);
            return result;
        }

        #endregion
    }
}
