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
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of handoff artifact persistence.
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
        /// <param name="connectionString">MySQL connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when connectionString is null or empty.</exception>
        public HandoffArtifactMethods(string connectionString)
        {
            _ConnectionString = !String.IsNullOrEmpty(connectionString) ? connectionString : throw new ArgumentNullException(nameof(connectionString));
            _WriteLock = null;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<HandoffArtifact> CreateAsync(HandoffArtifact artifact, CancellationToken token = default)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            artifact.LastUpdateUtc = DateTime.UtcNow;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, artifact, true), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return artifact;
        }

        /// <inheritdoc />
        public async Task<HandoffArtifact?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<HandoffArtifact> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM handoff_artifacts WHERE id = @id;",
                cmd => MysqlCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<HandoffArtifact> UpdateAsync(HandoffArtifact artifact, CancellationToken token = default)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            artifact.LastUpdateUtc = DateTime.UtcNow;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Update, cmd => Bind(cmd, artifact, false), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return artifact;
        }

        /// <inheritdoc />
        public async Task<HandoffArtifact?> ReadLatestByMissionAsync(string missionId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            List<HandoffArtifact> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM handoff_artifacts WHERE mission_id = @mission_id ORDER BY created_utc DESC, id DESC LIMIT 1;",
                cmd => MysqlCommandHelper.Add(cmd, "@mission_id", missionId), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<HandoffArtifact?> ReadByAttemptAsync(string missionId, string attemptId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            if (String.IsNullOrEmpty(attemptId)) throw new ArgumentNullException(nameof(attemptId));
            List<HandoffArtifact> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM handoff_artifacts WHERE mission_id = @mission_id AND attempt_id = @attempt_id ORDER BY created_utc DESC, id DESC LIMIT 1;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@mission_id", missionId);
                    MysqlCommandHelper.Add(cmd, "@attempt_id", attemptId);
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
        internal static async Task InsertAsync(MySqlConnection conn, MySqlTransaction tx, HandoffArtifact artifact, CancellationToken token)
        {
            artifact.LastUpdateUtc = DateTime.UtcNow;
            await MysqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, artifact, true), token).ConfigureAwait(false);
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

            Action<MySqlCommand> bind = cmd =>
            {
                if (query.TenantId != null) MysqlCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (query.MissionId != null) MysqlCommandHelper.Add(cmd, "@mission_id", query.MissionId);
                if (query.VoyageId != null) MysqlCommandHelper.Add(cmd, "@voyage_id", query.VoyageId);
            };

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await MysqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM handoff_artifacts" + where + ";", bind, token).ConfigureAwait(false);
                List<HandoffArtifact> rows = await MysqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM handoff_artifacts" + where + " ORDER BY created_utc DESC, id DESC" + MysqlCommandHelper.Page(offset, pageSize) + ";",
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
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                deleted = await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM handoff_artifacts WHERE mission_id = @mission_id;",
                    cmd => MysqlCommandHelper.Add(cmd, "@mission_id", missionId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static void Bind(MySqlCommand cmd, HandoffArtifact artifact, bool includeCreateColumns)
        {
            MysqlCommandHelper.Add(cmd, "@id", artifact.Id);
            if (includeCreateColumns)
            {
                MysqlCommandHelper.Add(cmd, "@tenant_id", artifact.TenantId);
                MysqlCommandHelper.AddDate(cmd, "@created_utc", artifact.CreatedUtc);
            }

            MysqlCommandHelper.Add(cmd, "@user_id", artifact.UserId);
            MysqlCommandHelper.Add(cmd, "@mission_id", artifact.MissionId);
            MysqlCommandHelper.Add(cmd, "@voyage_id", artifact.VoyageId);
            MysqlCommandHelper.Add(cmd, "@vessel_id", artifact.VesselId);
            MysqlCommandHelper.Add(cmd, "@stage_outcome_id", artifact.StageOutcomeId);
            MysqlCommandHelper.Add(cmd, "@attempt_id", artifact.AttemptId);
            MysqlCommandHelper.Add(cmd, "@persona", artifact.Persona);
            MysqlCommandHelper.Add(cmd, "@captain_id", artifact.CaptainId);
            MysqlCommandHelper.Add(cmd, "@captain_name", artifact.CaptainName);
            MysqlCommandHelper.Add(cmd, "@attempt", artifact.Attempt);
            MysqlCommandHelper.Add(cmd, "@rework_round", artifact.ReworkRound);
            MysqlCommandHelper.Add(cmd, "@schema_version", artifact.SchemaVersion);
            MysqlCommandHelper.Add(cmd, "@verdict", artifact.Verdict);
            MysqlCommandHelper.Add(cmd, "@branch_name", artifact.BranchName);
            MysqlCommandHelper.Add(cmd, "@commit_hash", artifact.CommitHash);
            MysqlCommandHelper.Add(cmd, "@findings_json", HandoffArtifactJson.Write(artifact.Findings));
            MysqlCommandHelper.Add(cmd, "@acceptance_criteria_json", HandoffArtifactJson.Write(artifact.AcceptanceCriteria));
            MysqlCommandHelper.Add(cmd, "@required_actions_json", HandoffArtifactJson.Write(artifact.RequiredActions));
            MysqlCommandHelper.Add(cmd, "@check_results_json", HandoffArtifactJson.Write(artifact.CheckResults));
            MysqlCommandHelper.Add(cmd, "@commit_references_json", HandoffArtifactJson.Write(artifact.CommitReferences));
            MysqlCommandHelper.Add(cmd, "@completion_lines_json", HandoffArtifactJson.Write(artifact.CompletionLines));
            MysqlCommandHelper.Add(cmd, "@output", artifact.Output);
            MysqlCommandHelper.Add(cmd, "@output_length", artifact.OutputLength);
            MysqlCommandHelper.Add(cmd, "@output_complete", artifact.OutputComplete);
            MysqlCommandHelper.Add(cmd, "@diff", artifact.Diff);
            MysqlCommandHelper.Add(cmd, "@diff_length", artifact.DiffLength);
            MysqlCommandHelper.Add(cmd, "@diff_complete", artifact.DiffComplete);
            MysqlCommandHelper.Add(cmd, "@diff_pointer", artifact.DiffPointer);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", artifact.LastUpdateUtc);
        }

        private static HandoffArtifact FromReader(MySqlDataReader reader)
        {
            HandoffArtifact artifact = new HandoffArtifact();
            artifact.Id = reader["id"].ToString()!;
            artifact.TenantId = MysqlCommandHelper.ReadString(reader["tenant_id"]);
            artifact.UserId = MysqlCommandHelper.ReadString(reader["user_id"]);
            artifact.MissionId = reader["mission_id"]?.ToString() ?? String.Empty;
            artifact.VoyageId = MysqlCommandHelper.ReadString(reader["voyage_id"]);
            artifact.VesselId = MysqlCommandHelper.ReadString(reader["vessel_id"]);
            artifact.StageOutcomeId = MysqlCommandHelper.ReadString(reader["stage_outcome_id"]);
            artifact.AttemptId = MysqlCommandHelper.ReadString(reader["attempt_id"]);
            artifact.Persona = MysqlCommandHelper.ReadString(reader["persona"]);
            artifact.CaptainId = MysqlCommandHelper.ReadString(reader["captain_id"]);
            artifact.CaptainName = MysqlCommandHelper.ReadString(reader["captain_name"]);
            artifact.Attempt = MysqlCommandHelper.ReadInt(reader["attempt"], 1);
            artifact.ReworkRound = MysqlCommandHelper.ReadInt(reader["rework_round"], 0);
            artifact.SchemaVersion = MysqlCommandHelper.ReadInt(reader["schema_version"], 1);
            artifact.Verdict = MysqlCommandHelper.ReadString(reader["verdict"]);
            artifact.BranchName = MysqlCommandHelper.ReadString(reader["branch_name"]);
            artifact.CommitHash = MysqlCommandHelper.ReadString(reader["commit_hash"]);
            artifact.Findings = HandoffArtifactJson.Read<HandoffFinding>(MysqlCommandHelper.ReadString(reader["findings_json"]));
            artifact.AcceptanceCriteria = HandoffArtifactJson.Read<string>(MysqlCommandHelper.ReadString(reader["acceptance_criteria_json"]));
            artifact.RequiredActions = HandoffArtifactJson.Read<string>(MysqlCommandHelper.ReadString(reader["required_actions_json"]));
            artifact.CheckResults = HandoffArtifactJson.Read<StageCheckResult>(MysqlCommandHelper.ReadString(reader["check_results_json"]));
            artifact.CommitReferences = HandoffArtifactJson.Read<string>(MysqlCommandHelper.ReadString(reader["commit_references_json"]));
            artifact.CompletionLines = HandoffArtifactJson.Read<string>(MysqlCommandHelper.ReadString(reader["completion_lines_json"]));
            artifact.Output = MysqlCommandHelper.ReadString(reader["output"]);
            artifact.OutputLength = MysqlCommandHelper.ReadInt(reader["output_length"], 0);
            artifact.OutputComplete = MysqlCommandHelper.ReadBool(reader["output_complete"], true);
            artifact.Diff = MysqlCommandHelper.ReadString(reader["diff"]);
            artifact.DiffLength = MysqlCommandHelper.ReadInt(reader["diff_length"], 0);
            artifact.DiffComplete = MysqlCommandHelper.ReadBool(reader["diff_complete"], true);
            artifact.DiffPointer = MysqlCommandHelper.ReadString(reader["diff_pointer"]);
            artifact.CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]);
            artifact.LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return artifact;
        }

        #endregion
    }
}
