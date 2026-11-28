namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQLite implementation of handoff artifact persistence.
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
        public HandoffArtifactMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<HandoffArtifact> CreateAsync(HandoffArtifact artifact, CancellationToken token = default)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            artifact.LastUpdateUtc = DateTime.UtcNow;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, artifact, true), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return artifact;
        }

        /// <inheritdoc />
        public async Task<HandoffArtifact?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<HandoffArtifact> rows = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM handoff_artifacts WHERE id = @id;",
                cmd => SqliteCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<HandoffArtifact> UpdateAsync(HandoffArtifact artifact, CancellationToken token = default)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            artifact.LastUpdateUtc = DateTime.UtcNow;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, _Update, cmd => Bind(cmd, artifact, false), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return artifact;
        }

        /// <inheritdoc />
        public async Task<HandoffArtifact?> ReadLatestByMissionAsync(string missionId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            List<HandoffArtifact> rows = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM handoff_artifacts WHERE mission_id = @mission_id ORDER BY created_utc DESC, id DESC LIMIT 1;",
                cmd => SqliteCommandHelper.Add(cmd, "@mission_id", missionId), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<HandoffArtifact?> ReadByAttemptAsync(string missionId, string attemptId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            if (String.IsNullOrEmpty(attemptId)) throw new ArgumentNullException(nameof(attemptId));
            List<HandoffArtifact> rows = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM handoff_artifacts WHERE mission_id = @mission_id AND attempt_id = @attempt_id ORDER BY created_utc DESC, id DESC LIMIT 1;",
                cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@mission_id", missionId);
                    SqliteCommandHelper.Add(cmd, "@attempt_id", attemptId);
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
        internal static async Task InsertAsync(SqliteConnection conn, SqliteTransaction tx, HandoffArtifact artifact, CancellationToken token)
        {
            artifact.LastUpdateUtc = DateTime.UtcNow;
            await SqliteCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, artifact, true), token).ConfigureAwait(false);
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

            Action<SqliteCommand> bind = cmd =>
            {
                if (query.TenantId != null) SqliteCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (query.MissionId != null) SqliteCommandHelper.Add(cmd, "@mission_id", query.MissionId);
                if (query.VoyageId != null) SqliteCommandHelper.Add(cmd, "@voyage_id", query.VoyageId);
            };

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (SqliteConnection conn = new SqliteProviderConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqliteCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM handoff_artifacts" + where + ";", bind, token).ConfigureAwait(false);
                List<HandoffArtifact> rows = await SqliteCommandHelper.QueryAsync(conn,
                    "SELECT * FROM handoff_artifacts" + where + " ORDER BY created_utc DESC, id DESC" + SqliteCommandHelper.Page(offset, pageSize) + ";",
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
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                deleted = await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM handoff_artifacts WHERE mission_id = @mission_id;",
                    cmd => SqliteCommandHelper.Add(cmd, "@mission_id", missionId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static void Bind(SqliteCommand cmd, HandoffArtifact artifact, bool includeCreateColumns)
        {
            SqliteCommandHelper.Add(cmd, "@id", artifact.Id);
            if (includeCreateColumns)
            {
                SqliteCommandHelper.Add(cmd, "@tenant_id", artifact.TenantId);
                SqliteCommandHelper.AddDate(cmd, "@created_utc", artifact.CreatedUtc);
            }

            SqliteCommandHelper.Add(cmd, "@user_id", artifact.UserId);
            SqliteCommandHelper.Add(cmd, "@mission_id", artifact.MissionId);
            SqliteCommandHelper.Add(cmd, "@voyage_id", artifact.VoyageId);
            SqliteCommandHelper.Add(cmd, "@vessel_id", artifact.VesselId);
            SqliteCommandHelper.Add(cmd, "@stage_outcome_id", artifact.StageOutcomeId);
            SqliteCommandHelper.Add(cmd, "@attempt_id", artifact.AttemptId);
            SqliteCommandHelper.Add(cmd, "@persona", artifact.Persona);
            SqliteCommandHelper.Add(cmd, "@captain_id", artifact.CaptainId);
            SqliteCommandHelper.Add(cmd, "@captain_name", artifact.CaptainName);
            SqliteCommandHelper.Add(cmd, "@attempt", artifact.Attempt);
            SqliteCommandHelper.Add(cmd, "@rework_round", artifact.ReworkRound);
            SqliteCommandHelper.Add(cmd, "@schema_version", artifact.SchemaVersion);
            SqliteCommandHelper.Add(cmd, "@verdict", artifact.Verdict);
            SqliteCommandHelper.Add(cmd, "@branch_name", artifact.BranchName);
            SqliteCommandHelper.Add(cmd, "@commit_hash", artifact.CommitHash);
            SqliteCommandHelper.Add(cmd, "@findings_json", HandoffArtifactJson.Write(artifact.Findings));
            SqliteCommandHelper.Add(cmd, "@acceptance_criteria_json", HandoffArtifactJson.Write(artifact.AcceptanceCriteria));
            SqliteCommandHelper.Add(cmd, "@required_actions_json", HandoffArtifactJson.Write(artifact.RequiredActions));
            SqliteCommandHelper.Add(cmd, "@check_results_json", HandoffArtifactJson.Write(artifact.CheckResults));
            SqliteCommandHelper.Add(cmd, "@commit_references_json", HandoffArtifactJson.Write(artifact.CommitReferences));
            SqliteCommandHelper.Add(cmd, "@completion_lines_json", HandoffArtifactJson.Write(artifact.CompletionLines));
            SqliteCommandHelper.Add(cmd, "@output", artifact.Output);
            SqliteCommandHelper.Add(cmd, "@output_length", artifact.OutputLength);
            SqliteCommandHelper.Add(cmd, "@output_complete", artifact.OutputComplete);
            SqliteCommandHelper.Add(cmd, "@diff", artifact.Diff);
            SqliteCommandHelper.Add(cmd, "@diff_length", artifact.DiffLength);
            SqliteCommandHelper.Add(cmd, "@diff_complete", artifact.DiffComplete);
            SqliteCommandHelper.Add(cmd, "@diff_pointer", artifact.DiffPointer);
            SqliteCommandHelper.AddDate(cmd, "@last_update_utc", artifact.LastUpdateUtc);
        }

        private static HandoffArtifact FromReader(SqliteDataReader reader)
        {
            HandoffArtifact artifact = new HandoffArtifact();
            artifact.Id = reader["id"].ToString()!;
            artifact.TenantId = SqliteCommandHelper.ReadString(reader["tenant_id"]);
            artifact.UserId = SqliteCommandHelper.ReadString(reader["user_id"]);
            artifact.MissionId = reader["mission_id"]?.ToString() ?? String.Empty;
            artifact.VoyageId = SqliteCommandHelper.ReadString(reader["voyage_id"]);
            artifact.VesselId = SqliteCommandHelper.ReadString(reader["vessel_id"]);
            artifact.StageOutcomeId = SqliteCommandHelper.ReadString(reader["stage_outcome_id"]);
            artifact.AttemptId = SqliteCommandHelper.ReadString(reader["attempt_id"]);
            artifact.Persona = SqliteCommandHelper.ReadString(reader["persona"]);
            artifact.CaptainId = SqliteCommandHelper.ReadString(reader["captain_id"]);
            artifact.CaptainName = SqliteCommandHelper.ReadString(reader["captain_name"]);
            artifact.Attempt = SqliteCommandHelper.ReadInt(reader["attempt"], 1);
            artifact.ReworkRound = SqliteCommandHelper.ReadInt(reader["rework_round"], 0);
            artifact.SchemaVersion = SqliteCommandHelper.ReadInt(reader["schema_version"], 1);
            artifact.Verdict = SqliteCommandHelper.ReadString(reader["verdict"]);
            artifact.BranchName = SqliteCommandHelper.ReadString(reader["branch_name"]);
            artifact.CommitHash = SqliteCommandHelper.ReadString(reader["commit_hash"]);
            artifact.Findings = HandoffArtifactJson.Read<HandoffFinding>(SqliteCommandHelper.ReadString(reader["findings_json"]));
            artifact.AcceptanceCriteria = HandoffArtifactJson.Read<string>(SqliteCommandHelper.ReadString(reader["acceptance_criteria_json"]));
            artifact.RequiredActions = HandoffArtifactJson.Read<string>(SqliteCommandHelper.ReadString(reader["required_actions_json"]));
            artifact.CheckResults = HandoffArtifactJson.Read<StageCheckResult>(SqliteCommandHelper.ReadString(reader["check_results_json"]));
            artifact.CommitReferences = HandoffArtifactJson.Read<string>(SqliteCommandHelper.ReadString(reader["commit_references_json"]));
            artifact.CompletionLines = HandoffArtifactJson.Read<string>(SqliteCommandHelper.ReadString(reader["completion_lines_json"]));
            artifact.Output = SqliteCommandHelper.ReadString(reader["output"]);
            artifact.OutputLength = SqliteCommandHelper.ReadInt(reader["output_length"], 0);
            artifact.OutputComplete = SqliteCommandHelper.ReadBool(reader["output_complete"], true);
            artifact.Diff = SqliteCommandHelper.ReadString(reader["diff"]);
            artifact.DiffLength = SqliteCommandHelper.ReadInt(reader["diff_length"], 0);
            artifact.DiffComplete = SqliteCommandHelper.ReadBool(reader["diff_complete"], true);
            artifact.DiffPointer = SqliteCommandHelper.ReadString(reader["diff_pointer"]);
            artifact.CreatedUtc = SqliteCommandHelper.ReadDate(reader["created_utc"]);
            artifact.LastUpdateUtc = SqliteCommandHelper.ReadDate(reader["last_update_utc"]);
            return artifact;
        }

        #endregion
    }
}
