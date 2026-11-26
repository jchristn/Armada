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
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// PostgreSQL implementation of handoff artifact persistence.
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
        public HandoffArtifactMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, artifact, true), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return artifact;
        }

        /// <inheritdoc />
        public async Task<HandoffArtifact?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<HandoffArtifact> rows = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM handoff_artifacts WHERE id = @id;",
                cmd => PostgresqlCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<HandoffArtifact> UpdateAsync(HandoffArtifact artifact, CancellationToken token = default)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            artifact.LastUpdateUtc = DateTime.UtcNow;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Update, cmd => Bind(cmd, artifact, false), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return artifact;
        }

        /// <inheritdoc />
        public async Task<HandoffArtifact?> ReadLatestByMissionAsync(string missionId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            List<HandoffArtifact> rows = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM handoff_artifacts WHERE mission_id = @mission_id ORDER BY created_utc DESC, id DESC LIMIT 1;",
                cmd => PostgresqlCommandHelper.Add(cmd, "@mission_id", missionId), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<HandoffArtifact?> ReadByAttemptAsync(string missionId, string attemptId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            if (String.IsNullOrEmpty(attemptId)) throw new ArgumentNullException(nameof(attemptId));
            List<HandoffArtifact> rows = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM handoff_artifacts WHERE mission_id = @mission_id AND attempt_id = @attempt_id ORDER BY created_utc DESC, id DESC LIMIT 1;",
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@mission_id", missionId);
                    PostgresqlCommandHelper.Add(cmd, "@attempt_id", attemptId);
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
        internal static async Task InsertAsync(NpgsqlConnection conn, NpgsqlTransaction tx, HandoffArtifact artifact, CancellationToken token)
        {
            artifact.LastUpdateUtc = DateTime.UtcNow;
            await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, artifact, true), token).ConfigureAwait(false);
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

            Action<NpgsqlCommand> bind = cmd =>
            {
                if (query.TenantId != null) PostgresqlCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (query.MissionId != null) PostgresqlCommandHelper.Add(cmd, "@mission_id", query.MissionId);
                if (query.VoyageId != null) PostgresqlCommandHelper.Add(cmd, "@voyage_id", query.VoyageId);
            };

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (NpgsqlConnection conn = new NpgsqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await PostgresqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM handoff_artifacts" + where + ";", bind, token).ConfigureAwait(false);
                List<HandoffArtifact> rows = await PostgresqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM handoff_artifacts" + where + " ORDER BY created_utc DESC, id DESC" + PostgresqlCommandHelper.Page(offset, pageSize) + ";",
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
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                deleted = await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM handoff_artifacts WHERE mission_id = @mission_id;",
                    cmd => PostgresqlCommandHelper.Add(cmd, "@mission_id", missionId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        #endregion

        #region Private-Methods

        private static void Bind(NpgsqlCommand cmd, HandoffArtifact artifact, bool includeCreateColumns)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", artifact.Id);
            if (includeCreateColumns)
            {
                PostgresqlCommandHelper.Add(cmd, "@tenant_id", artifact.TenantId);
                PostgresqlCommandHelper.AddDate(cmd, "@created_utc", artifact.CreatedUtc);
            }

            PostgresqlCommandHelper.Add(cmd, "@user_id", artifact.UserId);
            PostgresqlCommandHelper.Add(cmd, "@mission_id", artifact.MissionId);
            PostgresqlCommandHelper.Add(cmd, "@voyage_id", artifact.VoyageId);
            PostgresqlCommandHelper.Add(cmd, "@vessel_id", artifact.VesselId);
            PostgresqlCommandHelper.Add(cmd, "@stage_outcome_id", artifact.StageOutcomeId);
            PostgresqlCommandHelper.Add(cmd, "@attempt_id", artifact.AttemptId);
            PostgresqlCommandHelper.Add(cmd, "@persona", artifact.Persona);
            PostgresqlCommandHelper.Add(cmd, "@captain_id", artifact.CaptainId);
            PostgresqlCommandHelper.Add(cmd, "@captain_name", artifact.CaptainName);
            PostgresqlCommandHelper.Add(cmd, "@attempt", artifact.Attempt);
            PostgresqlCommandHelper.Add(cmd, "@rework_round", artifact.ReworkRound);
            PostgresqlCommandHelper.Add(cmd, "@schema_version", artifact.SchemaVersion);
            PostgresqlCommandHelper.Add(cmd, "@verdict", artifact.Verdict);
            PostgresqlCommandHelper.Add(cmd, "@branch_name", artifact.BranchName);
            PostgresqlCommandHelper.Add(cmd, "@commit_hash", artifact.CommitHash);
            PostgresqlCommandHelper.Add(cmd, "@findings_json", HandoffArtifactJson.Write(artifact.Findings));
            PostgresqlCommandHelper.Add(cmd, "@acceptance_criteria_json", HandoffArtifactJson.Write(artifact.AcceptanceCriteria));
            PostgresqlCommandHelper.Add(cmd, "@required_actions_json", HandoffArtifactJson.Write(artifact.RequiredActions));
            PostgresqlCommandHelper.Add(cmd, "@check_results_json", HandoffArtifactJson.Write(artifact.CheckResults));
            PostgresqlCommandHelper.Add(cmd, "@commit_references_json", HandoffArtifactJson.Write(artifact.CommitReferences));
            PostgresqlCommandHelper.Add(cmd, "@completion_lines_json", HandoffArtifactJson.Write(artifact.CompletionLines));
            PostgresqlCommandHelper.Add(cmd, "@output", artifact.Output);
            PostgresqlCommandHelper.Add(cmd, "@output_length", artifact.OutputLength);
            PostgresqlCommandHelper.Add(cmd, "@output_complete", artifact.OutputComplete);
            PostgresqlCommandHelper.Add(cmd, "@diff", artifact.Diff);
            PostgresqlCommandHelper.Add(cmd, "@diff_length", artifact.DiffLength);
            PostgresqlCommandHelper.Add(cmd, "@diff_complete", artifact.DiffComplete);
            PostgresqlCommandHelper.Add(cmd, "@diff_pointer", artifact.DiffPointer);
            PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", artifact.LastUpdateUtc);
        }

        private static HandoffArtifact FromReader(NpgsqlDataReader reader)
        {
            HandoffArtifact artifact = new HandoffArtifact();
            artifact.Id = reader["id"].ToString()!;
            artifact.TenantId = PostgresqlCommandHelper.ReadString(reader["tenant_id"]);
            artifact.UserId = PostgresqlCommandHelper.ReadString(reader["user_id"]);
            artifact.MissionId = reader["mission_id"]?.ToString() ?? String.Empty;
            artifact.VoyageId = PostgresqlCommandHelper.ReadString(reader["voyage_id"]);
            artifact.VesselId = PostgresqlCommandHelper.ReadString(reader["vessel_id"]);
            artifact.StageOutcomeId = PostgresqlCommandHelper.ReadString(reader["stage_outcome_id"]);
            artifact.AttemptId = PostgresqlCommandHelper.ReadString(reader["attempt_id"]);
            artifact.Persona = PostgresqlCommandHelper.ReadString(reader["persona"]);
            artifact.CaptainId = PostgresqlCommandHelper.ReadString(reader["captain_id"]);
            artifact.CaptainName = PostgresqlCommandHelper.ReadString(reader["captain_name"]);
            artifact.Attempt = PostgresqlCommandHelper.ReadInt(reader["attempt"], 1);
            artifact.ReworkRound = PostgresqlCommandHelper.ReadInt(reader["rework_round"], 0);
            artifact.SchemaVersion = PostgresqlCommandHelper.ReadInt(reader["schema_version"], 1);
            artifact.Verdict = PostgresqlCommandHelper.ReadString(reader["verdict"]);
            artifact.BranchName = PostgresqlCommandHelper.ReadString(reader["branch_name"]);
            artifact.CommitHash = PostgresqlCommandHelper.ReadString(reader["commit_hash"]);
            artifact.Findings = HandoffArtifactJson.Read<HandoffFinding>(PostgresqlCommandHelper.ReadString(reader["findings_json"]));
            artifact.AcceptanceCriteria = HandoffArtifactJson.Read<string>(PostgresqlCommandHelper.ReadString(reader["acceptance_criteria_json"]));
            artifact.RequiredActions = HandoffArtifactJson.Read<string>(PostgresqlCommandHelper.ReadString(reader["required_actions_json"]));
            artifact.CheckResults = HandoffArtifactJson.Read<StageCheckResult>(PostgresqlCommandHelper.ReadString(reader["check_results_json"]));
            artifact.CommitReferences = HandoffArtifactJson.Read<string>(PostgresqlCommandHelper.ReadString(reader["commit_references_json"]));
            artifact.CompletionLines = HandoffArtifactJson.Read<string>(PostgresqlCommandHelper.ReadString(reader["completion_lines_json"]));
            artifact.Output = PostgresqlCommandHelper.ReadString(reader["output"]);
            artifact.OutputLength = PostgresqlCommandHelper.ReadInt(reader["output_length"], 0);
            artifact.OutputComplete = PostgresqlCommandHelper.ReadBool(reader["output_complete"], true);
            artifact.Diff = PostgresqlCommandHelper.ReadString(reader["diff"]);
            artifact.DiffLength = PostgresqlCommandHelper.ReadInt(reader["diff_length"], 0);
            artifact.DiffComplete = PostgresqlCommandHelper.ReadBool(reader["diff_complete"], true);
            artifact.DiffPointer = PostgresqlCommandHelper.ReadString(reader["diff_pointer"]);
            artifact.CreatedUtc = PostgresqlCommandHelper.ReadDate(reader["created_utc"]);
            artifact.LastUpdateUtc = PostgresqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return artifact;
        }

        #endregion
    }
}
