namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.SqlClient;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQL Server implementation of handoff artifact persistence.
    /// </summary>
    public class HandoffArtifactMethods : IHandoffArtifactMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO handoff_artifacts
            (id, tenant_id, user_id, mission_id, voyage_id, vessel_id, stage_outcome_id, attempt_id, persona, captain_id, captain_name,
             attempt, rework_round, schema_version, verdict, branch_name, commit_hash, findings_json, acceptance_criteria_json,
             required_actions_json, check_results_json, commit_references_json, completion_lines_json, output, output_length,
             output_complete, diff, diff_length, diff_complete, diff_pointer, created_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @user_id, @mission_id, @voyage_id, @vessel_id, @stage_outcome_id, @attempt_id, @persona, @captain_id, @captain_name,
             @attempt, @rework_round, @schema_version, @verdict, @branch_name, @commit_hash, @findings_json, @acceptance_criteria_json,
             @required_actions_json, @check_results_json, @commit_references_json, @completion_lines_json, @output, @output_length,
             @output_complete, @diff, @diff_length, @diff_complete, @diff_pointer, @created_utc, @last_update_utc);";

        private static readonly string _Update = @"UPDATE handoff_artifacts SET
            user_id = @user_id, mission_id = @mission_id, voyage_id = @voyage_id, vessel_id = @vessel_id,
            stage_outcome_id = @stage_outcome_id, attempt_id = @attempt_id, persona = @persona, captain_id = @captain_id, captain_name = @captain_name,
            attempt = @attempt, rework_round = @rework_round, schema_version = @schema_version, verdict = @verdict,
            branch_name = @branch_name, commit_hash = @commit_hash, findings_json = @findings_json,
            acceptance_criteria_json = @acceptance_criteria_json, required_actions_json = @required_actions_json,
            check_results_json = @check_results_json, commit_references_json = @commit_references_json,
            completion_lines_json = @completion_lines_json, output = @output, output_length = @output_length,
            output_complete = @output_complete, diff = @diff, diff_length = @diff_length, diff_complete = @diff_complete,
            diff_pointer = @diff_pointer, last_update_utc = @last_update_utc
            WHERE id = @id;";

        private readonly string _ConnectionString;
        private readonly SemaphoreSlim? _WriteLock;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public HandoffArtifactMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<HandoffArtifact> CreateAsync(HandoffArtifact artifact, CancellationToken token = default)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            artifact.LastUpdateUtc = DateTime.UtcNow;
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, artifact, true), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return artifact;
        }

        /// <inheritdoc />
        public async Task<HandoffArtifact?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<HandoffArtifact> rows = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM handoff_artifacts WHERE id = @id;",
                cmd => SqlServerCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<HandoffArtifact> UpdateAsync(HandoffArtifact artifact, CancellationToken token = default)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            artifact.LastUpdateUtc = DateTime.UtcNow;
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, _Update, cmd => Bind(cmd, artifact, false), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return artifact;
        }

        /// <inheritdoc />
        public async Task<HandoffArtifact?> ReadLatestByMissionAsync(string missionId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            List<HandoffArtifact> rows = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT TOP 1 * FROM handoff_artifacts WHERE mission_id = @mission_id ORDER BY created_utc DESC, id DESC;",
                cmd => SqlServerCommandHelper.Add(cmd, "@mission_id", missionId), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<HandoffArtifact?> ReadByAttemptAsync(string missionId, string attemptId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            if (String.IsNullOrEmpty(attemptId)) throw new ArgumentNullException(nameof(attemptId));
            List<HandoffArtifact> rows = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT TOP 1 * FROM handoff_artifacts WHERE mission_id = @mission_id AND attempt_id = @attempt_id ORDER BY created_utc DESC, id DESC;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@mission_id", missionId);
                    SqlServerCommandHelper.Add(cmd, "@attempt_id", attemptId);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <summary>
        /// Insert an artifact inside a caller's transaction (the pipeline handoff batch).
        /// </summary>
        /// <param name="conn">Open connection.</param>
        /// <param name="tx">Transaction.</param>
        /// <param name="artifact">Artifact.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        internal static async Task InsertAsync(SqlConnection conn, SqlTransaction tx, HandoffArtifact artifact, CancellationToken token)
        {
            artifact.LastUpdateUtc = DateTime.UtcNow;
            await SqlServerCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, artifact, true), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<HandoffArtifact>> EnumerateAsync(HandoffArtifactQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            int pageSize = query.PageSize < 1 ? 1 : (query.PageSize > 1000 ? 1000 : query.PageSize);
            int pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
            int offset = (pageNumber - 1) * pageSize;

            List<string> conditions = new List<string>();
            if (query.TenantId != null) conditions.Add("tenant_id = @tenant_id");
            if (query.MissionId != null) conditions.Add("mission_id = @mission_id");
            if (query.VoyageId != null) conditions.Add("voyage_id = @voyage_id");

            Action<SqlCommand> bind = cmd =>
            {
                if (query.TenantId != null) SqlServerCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (query.MissionId != null) SqlServerCommandHelper.Add(cmd, "@mission_id", query.MissionId);
                if (query.VoyageId != null) SqlServerCommandHelper.Add(cmd, "@voyage_id", query.VoyageId);
            };

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqlServerCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM handoff_artifacts" + where + ";", bind, token).ConfigureAwait(false);
                List<HandoffArtifact> rows = await SqlServerCommandHelper.QueryAsync(conn,
                    "SELECT * FROM handoff_artifacts" + where + " ORDER BY created_utc DESC, id DESC" + SqlServerCommandHelper.Page(offset, pageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);

                EnumerationResult<HandoffArtifact> result = new EnumerationResult<HandoffArtifact>();
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalRecords = total;
                result.TotalPages = (int)Math.Ceiling((double)total / pageSize);
                result.Objects = rows;
                return result;
            }
        }

        /// <inheritdoc />
        public async Task<int> DeleteByMissionAsync(string missionId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            int deleted = 0;
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                deleted = await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM handoff_artifacts WHERE mission_id = @mission_id;",
                    cmd => SqlServerCommandHelper.Add(cmd, "@mission_id", missionId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static void Bind(SqlCommand cmd, HandoffArtifact artifact, bool includeCreateColumns)
        {
            SqlServerCommandHelper.Add(cmd, "@id", artifact.Id);
            if (includeCreateColumns)
            {
                SqlServerCommandHelper.Add(cmd, "@tenant_id", artifact.TenantId);
                SqlServerCommandHelper.AddDate(cmd, "@created_utc", artifact.CreatedUtc);
            }

            SqlServerCommandHelper.Add(cmd, "@user_id", artifact.UserId);
            SqlServerCommandHelper.Add(cmd, "@mission_id", artifact.MissionId);
            SqlServerCommandHelper.Add(cmd, "@voyage_id", artifact.VoyageId);
            SqlServerCommandHelper.Add(cmd, "@vessel_id", artifact.VesselId);
            SqlServerCommandHelper.Add(cmd, "@stage_outcome_id", artifact.StageOutcomeId);
            SqlServerCommandHelper.Add(cmd, "@attempt_id", artifact.AttemptId);
            SqlServerCommandHelper.Add(cmd, "@persona", artifact.Persona);
            SqlServerCommandHelper.Add(cmd, "@captain_id", artifact.CaptainId);
            SqlServerCommandHelper.Add(cmd, "@captain_name", artifact.CaptainName);
            SqlServerCommandHelper.Add(cmd, "@attempt", artifact.Attempt);
            SqlServerCommandHelper.Add(cmd, "@rework_round", artifact.ReworkRound);
            SqlServerCommandHelper.Add(cmd, "@schema_version", artifact.SchemaVersion);
            SqlServerCommandHelper.Add(cmd, "@verdict", artifact.Verdict);
            SqlServerCommandHelper.Add(cmd, "@branch_name", artifact.BranchName);
            SqlServerCommandHelper.Add(cmd, "@commit_hash", artifact.CommitHash);
            SqlServerCommandHelper.Add(cmd, "@findings_json", HandoffArtifactJson.Write(artifact.Findings));
            SqlServerCommandHelper.Add(cmd, "@acceptance_criteria_json", HandoffArtifactJson.Write(artifact.AcceptanceCriteria));
            SqlServerCommandHelper.Add(cmd, "@required_actions_json", HandoffArtifactJson.Write(artifact.RequiredActions));
            SqlServerCommandHelper.Add(cmd, "@check_results_json", HandoffArtifactJson.Write(artifact.CheckResults));
            SqlServerCommandHelper.Add(cmd, "@commit_references_json", HandoffArtifactJson.Write(artifact.CommitReferences));
            SqlServerCommandHelper.Add(cmd, "@completion_lines_json", HandoffArtifactJson.Write(artifact.CompletionLines));
            SqlServerCommandHelper.Add(cmd, "@output", artifact.Output);
            SqlServerCommandHelper.Add(cmd, "@output_length", artifact.OutputLength);
            SqlServerCommandHelper.Add(cmd, "@output_complete", artifact.OutputComplete);
            SqlServerCommandHelper.Add(cmd, "@diff", artifact.Diff);
            SqlServerCommandHelper.Add(cmd, "@diff_length", artifact.DiffLength);
            SqlServerCommandHelper.Add(cmd, "@diff_complete", artifact.DiffComplete);
            SqlServerCommandHelper.Add(cmd, "@diff_pointer", artifact.DiffPointer);
            SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", artifact.LastUpdateUtc);
        }

        private static HandoffArtifact FromReader(SqlDataReader reader)
        {
            HandoffArtifact artifact = new HandoffArtifact();
            artifact.Id = reader["id"].ToString()!;
            artifact.TenantId = SqlServerCommandHelper.ReadString(reader["tenant_id"]);
            artifact.UserId = SqlServerCommandHelper.ReadString(reader["user_id"]);
            artifact.MissionId = reader["mission_id"]?.ToString() ?? String.Empty;
            artifact.VoyageId = SqlServerCommandHelper.ReadString(reader["voyage_id"]);
            artifact.VesselId = SqlServerCommandHelper.ReadString(reader["vessel_id"]);
            artifact.StageOutcomeId = SqlServerCommandHelper.ReadString(reader["stage_outcome_id"]);
            artifact.AttemptId = SqlServerCommandHelper.ReadString(reader["attempt_id"]);
            artifact.Persona = SqlServerCommandHelper.ReadString(reader["persona"]);
            artifact.CaptainId = SqlServerCommandHelper.ReadString(reader["captain_id"]);
            artifact.CaptainName = SqlServerCommandHelper.ReadString(reader["captain_name"]);
            artifact.Attempt = SqlServerCommandHelper.ReadInt(reader["attempt"], 1);
            artifact.ReworkRound = SqlServerCommandHelper.ReadInt(reader["rework_round"], 0);
            artifact.SchemaVersion = SqlServerCommandHelper.ReadInt(reader["schema_version"], 1);
            artifact.Verdict = SqlServerCommandHelper.ReadString(reader["verdict"]);
            artifact.BranchName = SqlServerCommandHelper.ReadString(reader["branch_name"]);
            artifact.CommitHash = SqlServerCommandHelper.ReadString(reader["commit_hash"]);
            artifact.Findings = HandoffArtifactJson.Read<HandoffFinding>(SqlServerCommandHelper.ReadString(reader["findings_json"]));
            artifact.AcceptanceCriteria = HandoffArtifactJson.Read<string>(SqlServerCommandHelper.ReadString(reader["acceptance_criteria_json"]));
            artifact.RequiredActions = HandoffArtifactJson.Read<string>(SqlServerCommandHelper.ReadString(reader["required_actions_json"]));
            artifact.CheckResults = HandoffArtifactJson.Read<StageCheckResult>(SqlServerCommandHelper.ReadString(reader["check_results_json"]));
            artifact.CommitReferences = HandoffArtifactJson.Read<string>(SqlServerCommandHelper.ReadString(reader["commit_references_json"]));
            artifact.CompletionLines = HandoffArtifactJson.Read<string>(SqlServerCommandHelper.ReadString(reader["completion_lines_json"]));
            artifact.Output = SqlServerCommandHelper.ReadString(reader["output"]);
            artifact.OutputLength = SqlServerCommandHelper.ReadInt(reader["output_length"], 0);
            artifact.OutputComplete = SqlServerCommandHelper.ReadBool(reader["output_complete"], true);
            artifact.Diff = SqlServerCommandHelper.ReadString(reader["diff"]);
            artifact.DiffLength = SqlServerCommandHelper.ReadInt(reader["diff_length"], 0);
            artifact.DiffComplete = SqlServerCommandHelper.ReadBool(reader["diff_complete"], true);
            artifact.DiffPointer = SqlServerCommandHelper.ReadString(reader["diff_pointer"]);
            artifact.CreatedUtc = SqlServerCommandHelper.ReadDate(reader["created_utc"]);
            artifact.LastUpdateUtc = SqlServerCommandHelper.ReadDate(reader["last_update_utc"]);
            return artifact;
        }

        #endregion
    }
}
