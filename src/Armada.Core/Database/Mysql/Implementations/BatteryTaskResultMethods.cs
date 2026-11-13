namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of battery task result persistence.
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
        /// <param name="connectionString">MySQL connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when connectionString is null.</exception>
        public BatteryTaskResultMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _WriteLock = null;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<BatteryTaskResult> CreateAsync(BatteryTaskResult result, CancellationToken token = default)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, result), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return result;
        }

        /// <inheritdoc />
        public async Task<List<BatteryTaskResult>> ReadByRunAsync(string runId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(runId)) throw new ArgumentNullException(nameof(runId));
            return await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM battery_task_results WHERE run_id = @run_id ORDER BY created_utc ASC, BINARY id ASC;",
                cmd => MysqlCommandHelper.Add(cmd, "@run_id", runId), FromReader, token).ConfigureAwait(false);
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
            Action<MySqlCommand> bind = cmd =>
            {
                MysqlCommandHelper.Add(cmd, "@run_id", runId);
                if (!String.IsNullOrWhiteSpace(query.Status)) MysqlCommandHelper.Add(cmd, "@outcome", query.Status);
            };
            string direction = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await MysqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM battery_task_results" + where + ";", bind, token).ConfigureAwait(false);
                List<BatteryTaskResult> rows = await MysqlCommandHelper.QueryAsync(conn,
                    // Ordinal id tie-break: the default collation is case-insensitive, so ids that share a timestamp would sort
                    // differently from the other providers.
                    "SELECT * FROM battery_task_results" + where + " ORDER BY created_utc " + direction + ", BINARY id " + direction + MysqlCommandHelper.Page(offset, pageSize) + ";",
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
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                deleted = await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM battery_task_results WHERE run_id = @run_id;",
                    cmd => MysqlCommandHelper.Add(cmd, "@run_id", runId), token).ConfigureAwait(false);
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
            string? json = MysqlCommandHelper.ReadString(value);
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

        private static void Bind(MySqlCommand cmd, BatteryTaskResult result)
        {
            MysqlCommandHelper.Add(cmd, "@id", result.Id);
            MysqlCommandHelper.Add(cmd, "@run_id", result.RunId ?? String.Empty);
            MysqlCommandHelper.Add(cmd, "@tenant_id", result.TenantId);
            MysqlCommandHelper.Add(cmd, "@pack_id", result.PackId ?? String.Empty);
            MysqlCommandHelper.Add(cmd, "@pack_version", result.PackVersion);
            MysqlCommandHelper.Add(cmd, "@task_id", result.TaskId ?? String.Empty);
            MysqlCommandHelper.Add(cmd, "@task_title", result.TaskTitle);
            MysqlCommandHelper.Add(cmd, "@persona", result.Persona);
            MysqlCommandHelper.Add(cmd, "@grader", result.Grader.ToString());
            MysqlCommandHelper.Add(cmd, "@attempt", result.Attempt);
            MysqlCommandHelper.Add(cmd, "@outcome", result.Outcome.ToString());
            MysqlCommandHelper.Add(cmd, "@score", result.Score);
            MysqlCommandHelper.Add(cmd, "@summary", result.Summary);
            MysqlCommandHelper.Add(cmd, "@reason", result.Reason);
            MysqlCommandHelper.Add(cmd, "@details_json", SerializeDetails(result.Details));
            MysqlCommandHelper.Add(cmd, "@output", result.Output);
            MysqlCommandHelper.Add(cmd, "@vessel_id", result.VesselId);
            MysqlCommandHelper.Add(cmd, "@voyage_id", result.VoyageId);
            MysqlCommandHelper.Add(cmd, "@mission_id", result.MissionId);
            MysqlCommandHelper.Add(cmd, "@mission_status", result.MissionStatus);
            MysqlCommandHelper.Add(cmd, "@fixture_commit", result.FixtureCommit);
            MysqlCommandHelper.Add(cmd, "@duration_ms", result.DurationMs);
            MysqlCommandHelper.AddDate(cmd, "@created_utc", result.CreatedUtc);
            MysqlCommandHelper.AddDate(cmd, "@completed_utc", result.CompletedUtc);
        }

        private static BatteryTaskResult FromReader(MySqlDataReader reader)
        {
            BatteryTaskResult result = new BatteryTaskResult();
            result.Id = reader["id"].ToString()!;
            result.RunId = MysqlCommandHelper.ReadString(reader["run_id"]) ?? String.Empty;
            result.TenantId = MysqlCommandHelper.ReadString(reader["tenant_id"]);
            result.PackId = MysqlCommandHelper.ReadString(reader["pack_id"]) ?? String.Empty;
            result.PackVersion = MysqlCommandHelper.ReadInt(reader["pack_version"], 0);
            result.TaskId = MysqlCommandHelper.ReadString(reader["task_id"]) ?? String.Empty;
            result.TaskTitle = MysqlCommandHelper.ReadString(reader["task_title"]);
            result.Persona = MysqlCommandHelper.ReadString(reader["persona"]);
            result.Grader = MysqlCommandHelper.ReadEnum(reader["grader"], BatteryGraderTypeEnum.Command);
            result.Attempt = MysqlCommandHelper.ReadInt(reader["attempt"], 1);
            result.Outcome = MysqlCommandHelper.ReadEnum(reader["outcome"], BatteryTaskOutcomeEnum.Failed);
            result.Score = MysqlCommandHelper.ReadNullableDouble(reader["score"]);
            result.Summary = MysqlCommandHelper.ReadString(reader["summary"]);
            result.Reason = MysqlCommandHelper.ReadString(reader["reason"]);
            result.Details = DeserializeDetails(reader["details_json"]);
            result.Output = MysqlCommandHelper.ReadString(reader["output"]);
            result.VesselId = MysqlCommandHelper.ReadString(reader["vessel_id"]);
            result.VoyageId = MysqlCommandHelper.ReadString(reader["voyage_id"]);
            result.MissionId = MysqlCommandHelper.ReadString(reader["mission_id"]);
            result.MissionStatus = MysqlCommandHelper.ReadString(reader["mission_status"]);
            result.FixtureCommit = MysqlCommandHelper.ReadString(reader["fixture_commit"]);
            result.DurationMs = MysqlCommandHelper.ReadNullableLong(reader["duration_ms"]) ?? 0;
            result.CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]);
            result.CompletedUtc = MysqlCommandHelper.ReadNullableDate(reader["completed_utc"]);
            return result;
        }

        #endregion
    }
}
