namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQLite implementation of battery task result persistence.
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
        private readonly SqliteWriteGate _WriteGate;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SQLite database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public BatteryTaskResultMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _ConnectionString = driver.ConnectionString;
            _WriteGate = driver.WriteGate;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<BatteryTaskResult> CreateAsync(BatteryTaskResult result, CancellationToken token = default)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, result), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return result;
        }

        /// <inheritdoc />
        public async Task<List<BatteryTaskResult>> ReadByRunAsync(string runId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(runId)) throw new ArgumentNullException(nameof(runId));
            return await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM battery_task_results WHERE run_id = @run_id ORDER BY created_utc ASC, id ASC;",
                cmd => SqliteCommandHelper.Add(cmd, "@run_id", runId), FromReader, token).ConfigureAwait(false);
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
            Action<SqliteCommand> bind = cmd =>
            {
                SqliteCommandHelper.Add(cmd, "@run_id", runId);
                if (!String.IsNullOrWhiteSpace(query.Status)) SqliteCommandHelper.Add(cmd, "@outcome", query.Status);
            };
            string direction = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

            using (SqliteConnection conn = new SqliteProviderConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqliteCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM battery_task_results" + where + ";", bind, token).ConfigureAwait(false);
                List<BatteryTaskResult> rows = await SqliteCommandHelper.QueryAsync(conn,
                    "SELECT * FROM battery_task_results" + where + " ORDER BY created_utc " + direction + ", id " + direction + SqliteCommandHelper.Page(offset, pageSize) + ";",
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
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                deleted = await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM battery_task_results WHERE run_id = @run_id;",
                    cmd => SqliteCommandHelper.Add(cmd, "@run_id", runId), token).ConfigureAwait(false);
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
            string? json = SqliteCommandHelper.ReadString(value);
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

        private static void Bind(SqliteCommand cmd, BatteryTaskResult result)
        {
            SqliteCommandHelper.Add(cmd, "@id", result.Id);
            SqliteCommandHelper.Add(cmd, "@run_id", result.RunId ?? String.Empty);
            SqliteCommandHelper.Add(cmd, "@tenant_id", result.TenantId);
            SqliteCommandHelper.Add(cmd, "@pack_id", result.PackId ?? String.Empty);
            SqliteCommandHelper.Add(cmd, "@pack_version", result.PackVersion);
            SqliteCommandHelper.Add(cmd, "@task_id", result.TaskId ?? String.Empty);
            SqliteCommandHelper.Add(cmd, "@task_title", result.TaskTitle);
            SqliteCommandHelper.Add(cmd, "@persona", result.Persona);
            SqliteCommandHelper.Add(cmd, "@grader", result.Grader.ToString());
            SqliteCommandHelper.Add(cmd, "@attempt", result.Attempt);
            SqliteCommandHelper.Add(cmd, "@outcome", result.Outcome.ToString());
            SqliteCommandHelper.Add(cmd, "@score", result.Score);
            SqliteCommandHelper.Add(cmd, "@summary", result.Summary);
            SqliteCommandHelper.Add(cmd, "@reason", result.Reason);
            SqliteCommandHelper.Add(cmd, "@details_json", SerializeDetails(result.Details));
            SqliteCommandHelper.Add(cmd, "@output", result.Output);
            SqliteCommandHelper.Add(cmd, "@vessel_id", result.VesselId);
            SqliteCommandHelper.Add(cmd, "@voyage_id", result.VoyageId);
            SqliteCommandHelper.Add(cmd, "@mission_id", result.MissionId);
            SqliteCommandHelper.Add(cmd, "@mission_status", result.MissionStatus);
            SqliteCommandHelper.Add(cmd, "@fixture_commit", result.FixtureCommit);
            SqliteCommandHelper.Add(cmd, "@duration_ms", result.DurationMs);
            SqliteCommandHelper.AddDate(cmd, "@created_utc", result.CreatedUtc);
            SqliteCommandHelper.AddDate(cmd, "@completed_utc", result.CompletedUtc);
        }

        private static BatteryTaskResult FromReader(SqliteDataReader reader)
        {
            BatteryTaskResult result = new BatteryTaskResult();
            result.Id = reader["id"].ToString()!;
            result.RunId = SqliteCommandHelper.ReadString(reader["run_id"]) ?? String.Empty;
            result.TenantId = SqliteCommandHelper.ReadString(reader["tenant_id"]);
            result.PackId = SqliteCommandHelper.ReadString(reader["pack_id"]) ?? String.Empty;
            result.PackVersion = SqliteCommandHelper.ReadInt(reader["pack_version"], 0);
            result.TaskId = SqliteCommandHelper.ReadString(reader["task_id"]) ?? String.Empty;
            result.TaskTitle = SqliteCommandHelper.ReadString(reader["task_title"]);
            result.Persona = SqliteCommandHelper.ReadString(reader["persona"]);
            result.Grader = SqliteCommandHelper.ReadEnum(reader["grader"], BatteryGraderTypeEnum.Command);
            result.Attempt = SqliteCommandHelper.ReadInt(reader["attempt"], 1);
            result.Outcome = SqliteCommandHelper.ReadEnum(reader["outcome"], BatteryTaskOutcomeEnum.Failed);
            result.Score = SqliteCommandHelper.ReadNullableDouble(reader["score"]);
            result.Summary = SqliteCommandHelper.ReadString(reader["summary"]);
            result.Reason = SqliteCommandHelper.ReadString(reader["reason"]);
            result.Details = DeserializeDetails(reader["details_json"]);
            result.Output = SqliteCommandHelper.ReadString(reader["output"]);
            result.VesselId = SqliteCommandHelper.ReadString(reader["vessel_id"]);
            result.VoyageId = SqliteCommandHelper.ReadString(reader["voyage_id"]);
            result.MissionId = SqliteCommandHelper.ReadString(reader["mission_id"]);
            result.MissionStatus = SqliteCommandHelper.ReadString(reader["mission_status"]);
            result.FixtureCommit = SqliteCommandHelper.ReadString(reader["fixture_commit"]);
            result.DurationMs = SqliteCommandHelper.ReadNullableLong(reader["duration_ms"]) ?? 0;
            result.CreatedUtc = SqliteCommandHelper.ReadDate(reader["created_utc"]);
            result.CompletedUtc = SqliteCommandHelper.ReadNullableDate(reader["completed_utc"]);
            return result;
        }

        #endregion
    }
}
