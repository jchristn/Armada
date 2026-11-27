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
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQLite implementation of model profile persistence.
    /// </summary>
    public class ModelProfileMethods : IModelProfileMethods
    {
        #region Private-Members

        private static readonly string _InsertProfile = @"INSERT INTO model_profiles
            (id, tenant_id, identity_key, runtime, provider, model, served_context_tokens, served_context_source, tools_available, mcp_available,
             rendering_template, rendering_repeat_contract_at_end, rendering_max_files_per_mission,
             recovery_max_resumes, recovery_unclassified_resumes, recovery_transient_grace_minutes, recovery_judge_rework_rounds, recovery_output_repair_attempts, recovery_escalate_after_resumes,
             timeout_stall_minutes, timeout_tool_wait_minutes, timeout_max_runtime_minutes, timeout_first_output_minutes, speed_tokens_per_second, speed_first_output_ms, speed_measured_utc, tier, suggested_tier, suggested_tier_reason, suggested_tier_samples, suggested_tier_utc, notes, created_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @identity_key, @runtime, @provider, @model, @served_context_tokens, @served_context_source, @tools_available, @mcp_available,
             @rendering_template, @rendering_repeat_contract_at_end, @rendering_max_files_per_mission,
             @recovery_max_resumes, @recovery_unclassified_resumes, @recovery_transient_grace_minutes, @recovery_judge_rework_rounds, @recovery_output_repair_attempts, @recovery_escalate_after_resumes,
             @timeout_stall_minutes, @timeout_tool_wait_minutes, @timeout_max_runtime_minutes, @timeout_first_output_minutes, @speed_tokens_per_second, @speed_first_output_ms, @speed_measured_utc, @tier, @suggested_tier, @suggested_tier_reason, @suggested_tier_samples, @suggested_tier_utc, @notes, @created_utc, @last_update_utc);";

        private static readonly string _UpdateProfile = @"UPDATE model_profiles SET
            tools_available = @tools_available, mcp_available = @mcp_available, rendering_template = @rendering_template,
            rendering_repeat_contract_at_end = @rendering_repeat_contract_at_end,
            rendering_max_files_per_mission = @rendering_max_files_per_mission,
            recovery_max_resumes = @recovery_max_resumes, recovery_unclassified_resumes = @recovery_unclassified_resumes, recovery_transient_grace_minutes = @recovery_transient_grace_minutes, recovery_judge_rework_rounds = @recovery_judge_rework_rounds, recovery_output_repair_attempts = @recovery_output_repair_attempts, recovery_escalate_after_resumes = @recovery_escalate_after_resumes,
            timeout_stall_minutes = @timeout_stall_minutes, timeout_tool_wait_minutes = @timeout_tool_wait_minutes, timeout_max_runtime_minutes = @timeout_max_runtime_minutes, timeout_first_output_minutes = @timeout_first_output_minutes, speed_tokens_per_second = @speed_tokens_per_second, speed_first_output_ms = @speed_first_output_ms, speed_measured_utc = @speed_measured_utc, tier = @tier, suggested_tier = @suggested_tier, suggested_tier_reason = @suggested_tier_reason, suggested_tier_samples = @suggested_tier_samples, suggested_tier_utc = @suggested_tier_utc, notes = @notes, last_update_utc = @last_update_utc
            WHERE id = @id;";

        private static readonly string _InsertRevision = @"INSERT INTO model_profile_revisions
            (id, profile_id, tenant_id, runtime_version, model_digest, sampling_temperature, sampling_top_p, tool_tools, tool_mcp, first_seen_utc, evaluation_run_id, evaluation_report_json)
            VALUES
            (@id, @profile_id, @tenant_id, @runtime_version, @model_digest, @sampling_temperature, @sampling_top_p, @tool_tools, @tool_mcp, @first_seen_utc, @evaluation_run_id, @evaluation_report_json);";

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
        public ModelProfileMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<ModelProfile> CreateAsync(ModelProfile profile, string identityKey, CancellationToken token = default)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (String.IsNullOrEmpty(identityKey)) throw new ArgumentNullException(nameof(identityKey));
            profile.LastUpdateUtc = DateTime.UtcNow;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, _InsertProfile, cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", profile.TenantId);
                    SqliteCommandHelper.Add(cmd, "@identity_key", identityKey);
                    SqliteCommandHelper.Add(cmd, "@runtime", profile.Identity.Runtime ?? String.Empty);
                    SqliteCommandHelper.Add(cmd, "@provider", profile.Identity.Provider);
                    SqliteCommandHelper.Add(cmd, "@model", profile.Identity.Model);
                    SqliteCommandHelper.Add(cmd, "@served_context_tokens", profile.Identity.ServedContextTokens);
                    SqliteCommandHelper.Add(cmd, "@served_context_source", profile.Identity.ServedContextSource.HasValue ? profile.Identity.ServedContextSource.Value.ToString() : null);
                    SqliteCommandHelper.AddDate(cmd, "@created_utc", profile.CreatedUtc);
                    BindMutable(cmd, profile);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return profile;
        }

        /// <inheritdoc />
        public async Task<ModelProfile?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<ModelProfile> rows = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM model_profiles WHERE id = @id;",
                cmd => SqliteCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<Dictionary<string, ModelProfile>> ReadManyAsync(IEnumerable<string?> ids, CancellationToken token = default)
        {
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            List<string> distinct = ids.Where(id => !String.IsNullOrEmpty(id)).Select(id => id!).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, ModelProfile> result = new Dictionary<string, ModelProfile>(StringComparer.Ordinal);
            for (int offset = 0; offset < distinct.Count; offset += 500)
            {
                List<string> chunk = distinct.Skip(offset).Take(500).ToList();
                string names = String.Join(", ", chunk.Select((string id, int i) => "@id" + i));
                List<ModelProfile> rows = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                    "SELECT * FROM model_profiles WHERE id IN (" + names + ");",
                    cmd =>
                    {
                        for (int i = 0; i < chunk.Count; i++) SqliteCommandHelper.Add(cmd, "@id" + i, chunk[i]);
                    }, FromReader, token).ConfigureAwait(false);
                foreach (ModelProfile row in rows) result[row.Id] = row;
            }
            return result;
        }

        /// <inheritdoc />
        public async Task<ModelProfile?> ReadByIdentityKeyAsync(string identityKey, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(identityKey)) throw new ArgumentNullException(nameof(identityKey));
            List<ModelProfile> rows = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM model_profiles WHERE identity_key = @identity_key;",
                cmd => SqliteCommandHelper.Add(cmd, "@identity_key", identityKey), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<ModelProfile> UpdateAsync(ModelProfile profile, CancellationToken token = default)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            profile.LastUpdateUtc = DateTime.UtcNow;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, _UpdateProfile, cmd => BindMutable(cmd, profile), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return profile;
        }

        /// <inheritdoc />
        public async Task<bool> UpdateProbedCapabilitiesAsync(string profileId, bool? toolsAvailable, bool? mcpAvailable, DateTime probedUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(profileId)) throw new ArgumentNullException(nameof(profileId));
            int changed = 0;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                changed = await SqliteCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE model_profiles SET probed_tools_available = @probed_tools_available, probed_mcp_available = @probed_mcp_available, capabilities_probed_utc = @capabilities_probed_utc WHERE id = @id;",
                    cmd =>
                    {
                        SqliteCommandHelper.Add(cmd, "@id", profileId);
                        SqliteCommandHelper.Add(cmd, "@probed_tools_available", toolsAvailable);
                        SqliteCommandHelper.Add(cmd, "@probed_mcp_available", mcpAvailable);
                        SqliteCommandHelper.AddDate(cmd, "@capabilities_probed_utc", probedUtc);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return changed == 1;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<ModelProfile>> EnumerateAsync(ModelProfileQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            int pageSize = query.PageSize < 1 ? 1 : (query.PageSize > 1000 ? 1000 : query.PageSize);
            int pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
            int offset = (pageNumber - 1) * pageSize;

            List<string> conditions = new List<string>();
            if (query.TenantId != null) conditions.Add("tenant_id = @tenant_id");
            if (query.Runtime != null) conditions.Add("LOWER(runtime) = @runtime");
            if (query.Provider != null) conditions.Add("LOWER(provider) = @provider");
            if (query.ModelContains != null) conditions.Add("LOWER(model) LIKE @model ESCAPE '!'");

            Action<SqliteCommand> bind = cmd =>
            {
                if (query.TenantId != null) SqliteCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (query.Runtime != null) SqliteCommandHelper.Add(cmd, "@runtime", query.Runtime.ToLowerInvariant());
                if (query.Provider != null) SqliteCommandHelper.Add(cmd, "@provider", query.Provider.ToLowerInvariant());
                if (query.ModelContains != null) SqliteCommandHelper.Add(cmd, "@model", VesselHealthQueryBuilder.ContainsPattern(query.ModelContains));
            };

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (SqliteConnection conn = new SqliteProviderConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqliteCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM model_profiles" + where + ";", bind, token).ConfigureAwait(false);
                List<ModelProfile> rows = await SqliteCommandHelper.QueryAsync(conn,
                    "SELECT * FROM model_profiles" + where + " ORDER BY created_utc DESC, id DESC" + SqliteCommandHelper.Page(offset, pageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);

                EnumerationResult<ModelProfile> result = new EnumerationResult<ModelProfile>();
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalRecords = total;
                result.TotalPages = (int)Math.Ceiling((double)total / pageSize);
                result.Objects = rows;
                return result;
            }
        }

        /// <inheritdoc />
        public async Task<ModelProfileRevision> CreateRevisionAsync(ModelProfileRevision revision, CancellationToken token = default)
        {
            if (revision == null) throw new ArgumentNullException(nameof(revision));
            if (String.IsNullOrEmpty(revision.ProfileId)) throw new ArgumentException("ProfileId is required.", nameof(revision));
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, _InsertRevision, cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@id", revision.Id);
                    SqliteCommandHelper.Add(cmd, "@profile_id", revision.ProfileId);
                    SqliteCommandHelper.Add(cmd, "@tenant_id", revision.TenantId);
                    SqliteCommandHelper.Add(cmd, "@runtime_version", revision.RuntimeVersion);
                    SqliteCommandHelper.Add(cmd, "@model_digest", revision.ModelDigest);
                    SqliteCommandHelper.Add(cmd, "@sampling_temperature", revision.Sampling.Temperature);
                    SqliteCommandHelper.Add(cmd, "@sampling_top_p", revision.Sampling.TopP);
                    SqliteCommandHelper.Add(cmd, "@tool_tools", revision.ToolConfiguration.Tools);
                    SqliteCommandHelper.Add(cmd, "@tool_mcp", revision.ToolConfiguration.Mcp);
                    SqliteCommandHelper.AddDate(cmd, "@first_seen_utc", revision.FirstSeenUtc);
                    SqliteCommandHelper.Add(cmd, "@evaluation_run_id", revision.EvaluationRunId);
                    SqliteCommandHelper.Add(cmd, "@evaluation_report_json", ModelEvaluationReport.ToJson(revision.EvaluationReport));
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            revision.CompatibleWith = new List<string>();
            return revision;
        }

        /// <inheritdoc />
        public async Task<ModelProfileRevision?> ReadRevisionAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            using (SqliteConnection conn = new SqliteProviderConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                List<ModelProfileRevision> rows = await SqliteCommandHelper.QueryAsync(conn,
                    "SELECT * FROM model_profile_revisions WHERE id = @id;",
                    cmd => SqliteCommandHelper.Add(cmd, "@id", id), RevisionFromReader, token).ConfigureAwait(false);
                ModelProfileRevision? revision = rows.FirstOrDefault();
                if (revision == null) return null;
                List<string[]> marks = await SqliteCommandHelper.QueryAsync(conn,
                    "SELECT revision_id, compatible_revision_id FROM model_profile_revision_compat WHERE revision_id = @id ORDER BY compatible_revision_id;",
                    cmd => SqliteCommandHelper.Add(cmd, "@id", id), CompatFromReader, token).ConfigureAwait(false);
                revision.CompatibleWith = marks.Select(m => m[1]).ToList();
                return revision;
            }
        }

        /// <inheritdoc />
        public async Task<List<ModelProfileRevision>> ReadRevisionsAsync(string profileId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(profileId)) throw new ArgumentNullException(nameof(profileId));
            using (SqliteConnection conn = new SqliteProviderConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                List<ModelProfileRevision> rows = await SqliteCommandHelper.QueryAsync(conn,
                    "SELECT * FROM model_profile_revisions WHERE profile_id = @profile_id ORDER BY first_seen_utc ASC, id ASC;",
                    cmd => SqliteCommandHelper.Add(cmd, "@profile_id", profileId), RevisionFromReader, token).ConfigureAwait(false);
                if (rows.Count == 0) return rows;
                List<string[]> marks = await SqliteCommandHelper.QueryAsync(conn,
                    "SELECT c.revision_id, c.compatible_revision_id FROM model_profile_revision_compat c "
                    + "INNER JOIN model_profile_revisions r ON r.id = c.revision_id WHERE r.profile_id = @profile_id "
                    + "ORDER BY c.revision_id, c.compatible_revision_id;",
                    cmd => SqliteCommandHelper.Add(cmd, "@profile_id", profileId), CompatFromReader, token).ConfigureAwait(false);
                foreach (ModelProfileRevision revision in rows)
                    revision.CompatibleWith = marks.Where(m => m[0] == revision.Id).Select(m => m[1]).ToList();
                return rows;
            }
        }

        /// <inheritdoc />
        public async Task SetCompatibleWithAsync(string revisionId, IEnumerable<string> compatibleWith, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(revisionId)) throw new ArgumentNullException(nameof(revisionId));
            if (compatibleWith == null) throw new ArgumentNullException(nameof(compatibleWith));
            List<string> others = compatibleWith
                .Where(id => !String.IsNullOrEmpty(id) && !String.Equals(id, revisionId, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal).ToList();
            DateTime now = DateTime.UtcNow;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx,
                    "DELETE FROM model_profile_revision_compat WHERE revision_id = @id OR compatible_revision_id = @id;",
                    cmd => SqliteCommandHelper.Add(cmd, "@id", revisionId), token).ConfigureAwait(false);
                foreach (string other in others)
                {
                    await InsertMarkAsync(conn, tx, revisionId, other, now, token).ConfigureAwait(false);
                    await InsertMarkAsync(conn, tx, other, revisionId, now, token).ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> LinkCaptainAsync(string captainId, string? profileId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));
            int updated = 0;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                updated = await SqliteCommandHelper.ExecuteAsync(conn, tx, "UPDATE captains SET model_profile_id = @profile_id WHERE id = @id;", cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@id", captainId);
                    SqliteCommandHelper.Add(cmd, "@profile_id", String.IsNullOrEmpty(profileId) ? null : profileId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return updated > 0;
        }

        #endregion

        #region Private-Methods

        private static async Task InsertMarkAsync(SqliteConnection conn, SqliteTransaction tx, string revisionId, string otherId, DateTime now, CancellationToken token)
        {
            await SqliteCommandHelper.ExecuteAsync(conn, tx,
                "INSERT INTO model_profile_revision_compat (revision_id, compatible_revision_id, created_utc) VALUES (@revision_id, @compatible_revision_id, @created_utc);",
                cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@revision_id", revisionId);
                    SqliteCommandHelper.Add(cmd, "@compatible_revision_id", otherId);
                    SqliteCommandHelper.AddDate(cmd, "@created_utc", now);
                }, token).ConfigureAwait(false);
        }

        private static void BindMutable(SqliteCommand cmd, ModelProfile profile)
        {
            SqliteCommandHelper.Add(cmd, "@id", profile.Id);
            SqliteCommandHelper.Add(cmd, "@tools_available", profile.Capabilities.ToolsAvailable);
            SqliteCommandHelper.Add(cmd, "@mcp_available", profile.Capabilities.McpAvailable);
            SqliteCommandHelper.Add(cmd, "@rendering_template", profile.Rendering.Template);
            SqliteCommandHelper.Add(cmd, "@rendering_repeat_contract_at_end", profile.Rendering.RepeatContractAtEnd);
            SqliteCommandHelper.Add(cmd, "@rendering_max_files_per_mission", profile.Rendering.MaxFilesPerMission);
            SqliteCommandHelper.Add(cmd, "@recovery_max_resumes", profile.Recovery.MaxResumes);
            SqliteCommandHelper.Add(cmd, "@recovery_unclassified_resumes", profile.Recovery.UnclassifiedResumes);
            SqliteCommandHelper.Add(cmd, "@recovery_transient_grace_minutes", profile.Recovery.TransientGraceMinutes);
            SqliteCommandHelper.Add(cmd, "@recovery_judge_rework_rounds", profile.Recovery.JudgeReworkRounds);
            SqliteCommandHelper.Add(cmd, "@recovery_output_repair_attempts", profile.Recovery.OutputRepairAttempts);
            SqliteCommandHelper.Add(cmd, "@recovery_escalate_after_resumes", profile.Recovery.EscalateAfterResumes);
            SqliteCommandHelper.Add(cmd, "@timeout_stall_minutes", profile.Timeouts.StallThresholdMinutes);
            SqliteCommandHelper.Add(cmd, "@timeout_tool_wait_minutes", profile.Timeouts.ToolWaitHardLimitMinutes);
            SqliteCommandHelper.Add(cmd, "@timeout_max_runtime_minutes", profile.Timeouts.MaxRuntimeMinutes);
            SqliteCommandHelper.Add(cmd, "@timeout_first_output_minutes", profile.Timeouts.FirstOutputMinutes);
            SqliteCommandHelper.Add(cmd, "@speed_tokens_per_second", profile.Speed.TokensPerSecond);
            SqliteCommandHelper.Add(cmd, "@speed_first_output_ms", profile.Speed.FirstOutputMs);
            SqliteCommandHelper.AddDate(cmd, "@speed_measured_utc", profile.Speed.MeasuredUtc);
            SqliteCommandHelper.Add(cmd, "@tier", profile.Tiering.Tier.HasValue ? profile.Tiering.Tier.Value.ToString() : null);
            SqliteCommandHelper.Add(cmd, "@suggested_tier", profile.Tiering.SuggestedTier.HasValue ? profile.Tiering.SuggestedTier.Value.ToString() : null);
            SqliteCommandHelper.Add(cmd, "@suggested_tier_reason", profile.Tiering.SuggestedReason);
            SqliteCommandHelper.Add(cmd, "@suggested_tier_samples", profile.Tiering.SuggestedSamples);
            SqliteCommandHelper.AddDate(cmd, "@suggested_tier_utc", profile.Tiering.SuggestedUtc);
            SqliteCommandHelper.Add(cmd, "@notes", profile.Notes);
            SqliteCommandHelper.AddDate(cmd, "@last_update_utc", profile.LastUpdateUtc);
        }

        private static CaptainTierEnum? ReadTier(string? value)
        {
            return Enum.TryParse(value, true, out CaptainTierEnum tier) && Enum.IsDefined(tier) ? tier : null;
        }

        private static int? ReadRecovery(int? value, int min, int max)
        {
            return value.HasValue && value.Value >= min && value.Value <= max ? value : null;
        }

        private static ModelProfile FromReader(SqliteDataReader reader)
        {
            ModelProfile profile = new ModelProfile();
            profile.Id = reader["id"].ToString()!;
            profile.TenantId = SqliteCommandHelper.ReadString(reader["tenant_id"]);
            profile.Identity.Runtime = SqliteCommandHelper.ReadString(reader["runtime"]) ?? String.Empty;
            profile.Identity.Provider = SqliteCommandHelper.ReadString(reader["provider"]);
            profile.Identity.Model = SqliteCommandHelper.ReadString(reader["model"]);
            profile.Identity.ServedContextTokens = SqliteCommandHelper.ReadNullableInt(reader["served_context_tokens"]);
            profile.Identity.ServedContextSource = Enum.TryParse(SqliteCommandHelper.ReadString(reader["served_context_source"]), true, out ContextLengthSourceEnum contextSource) ? contextSource : null;
            profile.Capabilities.ToolsAvailable = SqliteCommandHelper.ReadNullableBool(reader["tools_available"]);
            profile.Capabilities.McpAvailable = SqliteCommandHelper.ReadNullableBool(reader["mcp_available"]);
            profile.Capabilities.ProbedToolsAvailable = SqliteCommandHelper.ReadNullableBool(reader["probed_tools_available"]);
            profile.Capabilities.ProbedMcpAvailable = SqliteCommandHelper.ReadNullableBool(reader["probed_mcp_available"]);
            profile.Capabilities.ProbedUtc = SqliteCommandHelper.ReadNullableDate(reader["capabilities_probed_utc"]);
            profile.Rendering.Template = SqliteCommandHelper.ReadString(reader["rendering_template"]);
            profile.Rendering.RepeatContractAtEnd = SqliteCommandHelper.ReadNullableBool(reader["rendering_repeat_contract_at_end"]);
            int? maxFiles = SqliteCommandHelper.ReadNullableInt(reader["rendering_max_files_per_mission"]);
            profile.Rendering.MaxFilesPerMission = maxFiles.HasValue && maxFiles.Value >= 1 ? maxFiles : null;
            profile.Recovery.MaxResumes = ReadRecovery(SqliteCommandHelper.ReadNullableInt(reader["recovery_max_resumes"]), 0, 10);
            profile.Recovery.UnclassifiedResumes = ReadRecovery(SqliteCommandHelper.ReadNullableInt(reader["recovery_unclassified_resumes"]), 0, 3);
            profile.Recovery.TransientGraceMinutes = ReadRecovery(SqliteCommandHelper.ReadNullableInt(reader["recovery_transient_grace_minutes"]), 0, 120);
            profile.Recovery.JudgeReworkRounds = ReadRecovery(SqliteCommandHelper.ReadNullableInt(reader["recovery_judge_rework_rounds"]), 1, 5);
            profile.Recovery.OutputRepairAttempts = ReadRecovery(SqliteCommandHelper.ReadNullableInt(reader["recovery_output_repair_attempts"]), 0, 3);
            profile.Recovery.EscalateAfterResumes = ReadRecovery(SqliteCommandHelper.ReadNullableInt(reader["recovery_escalate_after_resumes"]), 0, 10);
            profile.Timeouts.StallThresholdMinutes = ReadRecovery(SqliteCommandHelper.ReadNullableInt(reader["timeout_stall_minutes"]), 1, 240);
            profile.Timeouts.ToolWaitHardLimitMinutes = ReadRecovery(SqliteCommandHelper.ReadNullableInt(reader["timeout_tool_wait_minutes"]), 1, 1440);
            profile.Timeouts.MaxRuntimeMinutes = ReadRecovery(SqliteCommandHelper.ReadNullableInt(reader["timeout_max_runtime_minutes"]), 1, 2880);
            profile.Timeouts.FirstOutputMinutes = ReadRecovery(SqliteCommandHelper.ReadNullableInt(reader["timeout_first_output_minutes"]), 1, 240);
            profile.Speed.TokensPerSecond = SqliteCommandHelper.ReadNullableDouble(reader["speed_tokens_per_second"]);
            profile.Speed.FirstOutputMs = SqliteCommandHelper.ReadNullableLong(reader["speed_first_output_ms"]);
            profile.Speed.MeasuredUtc = SqliteCommandHelper.ReadNullableDate(reader["speed_measured_utc"]);
            profile.Tiering.Tier = ReadTier(SqliteCommandHelper.ReadString(reader["tier"]));
            profile.Tiering.SuggestedTier = ReadTier(SqliteCommandHelper.ReadString(reader["suggested_tier"]));
            profile.Tiering.SuggestedReason = SqliteCommandHelper.ReadString(reader["suggested_tier_reason"]);
            profile.Tiering.SuggestedSamples = SqliteCommandHelper.ReadNullableInt(reader["suggested_tier_samples"]);
            profile.Tiering.SuggestedUtc = SqliteCommandHelper.ReadNullableDate(reader["suggested_tier_utc"]);
            profile.Notes = SqliteCommandHelper.ReadString(reader["notes"]);
            profile.CreatedUtc = SqliteCommandHelper.ReadDate(reader["created_utc"]);
            profile.LastUpdateUtc = SqliteCommandHelper.ReadDate(reader["last_update_utc"]);
            return profile;
        }

        private static ModelProfileRevision RevisionFromReader(SqliteDataReader reader)
        {
            ModelProfileRevision revision = new ModelProfileRevision();
            revision.Id = reader["id"].ToString()!;
            revision.ProfileId = reader["profile_id"].ToString()!;
            revision.TenantId = SqliteCommandHelper.ReadString(reader["tenant_id"]);
            revision.RuntimeVersion = SqliteCommandHelper.ReadString(reader["runtime_version"]);
            revision.ModelDigest = SqliteCommandHelper.ReadString(reader["model_digest"]);
            revision.Sampling.Temperature = SqliteCommandHelper.ReadNullableDouble(reader["sampling_temperature"]);
            revision.Sampling.TopP = SqliteCommandHelper.ReadNullableDouble(reader["sampling_top_p"]);
            revision.ToolConfiguration.Tools = SqliteCommandHelper.ReadNullableBool(reader["tool_tools"]);
            revision.ToolConfiguration.Mcp = SqliteCommandHelper.ReadNullableBool(reader["tool_mcp"]);
            revision.FirstSeenUtc = SqliteCommandHelper.ReadDate(reader["first_seen_utc"]);
            revision.EvaluationRunId = SqliteCommandHelper.ReadString(reader["evaluation_run_id"]);
            revision.EvaluationReport = ModelEvaluationReport.FromJson(SqliteCommandHelper.ReadString(reader["evaluation_report_json"]));
            return revision;
        }

        private static string[] CompatFromReader(SqliteDataReader reader)
        {
            return new string[] { reader["revision_id"].ToString()!, reader["compatible_revision_id"].ToString()! };
        }

        #endregion
    }
}
