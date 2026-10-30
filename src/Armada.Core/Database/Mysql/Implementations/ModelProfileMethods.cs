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
    /// MySQL implementation of model profile persistence.
    /// </summary>
    public class ModelProfileMethods : IModelProfileMethods
    {
        #region Private-Members

        private static readonly string _InsertProfile = @"INSERT INTO model_profiles
            (id, tenant_id, identity_key, runtime, provider, model, served_context_tokens, tools_available, mcp_available,
             rendering_template, rendering_repeat_contract_at_end, rendering_max_files_per_mission, notes, created_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @identity_key, @runtime, @provider, @model, @served_context_tokens, @tools_available, @mcp_available,
             @rendering_template, @rendering_repeat_contract_at_end, @rendering_max_files_per_mission, @notes, @created_utc, @last_update_utc);";

        private static readonly string _UpdateProfile = @"UPDATE model_profiles SET
            tools_available = @tools_available, mcp_available = @mcp_available, rendering_template = @rendering_template,
            rendering_repeat_contract_at_end = @rendering_repeat_contract_at_end,
            rendering_max_files_per_mission = @rendering_max_files_per_mission, notes = @notes, last_update_utc = @last_update_utc
            WHERE id = @id;";

        private static readonly string _InsertRevision = @"INSERT INTO model_profile_revisions
            (id, profile_id, tenant_id, runtime_version, model_digest, sampling_temperature, sampling_top_p, tool_tools, tool_mcp, first_seen_utc)
            VALUES
            (@id, @profile_id, @tenant_id, @runtime_version, @model_digest, @sampling_temperature, @sampling_top_p, @tool_tools, @tool_mcp, @first_seen_utc);";

        private readonly string _ConnectionString;
        private readonly SemaphoreSlim? _WriteLock;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when connectionString is null.</exception>
        public ModelProfileMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _WriteLock = null;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<ModelProfile> CreateAsync(ModelProfile profile, string identityKey, CancellationToken token = default)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (String.IsNullOrEmpty(identityKey)) throw new ArgumentNullException(nameof(identityKey));
            profile.LastUpdateUtc = DateTime.UtcNow;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _InsertProfile, cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", profile.TenantId);
                    MysqlCommandHelper.Add(cmd, "@identity_key", identityKey);
                    MysqlCommandHelper.Add(cmd, "@runtime", profile.Identity.Runtime ?? String.Empty);
                    MysqlCommandHelper.Add(cmd, "@provider", profile.Identity.Provider);
                    MysqlCommandHelper.Add(cmd, "@model", profile.Identity.Model);
                    MysqlCommandHelper.Add(cmd, "@served_context_tokens", profile.Identity.ServedContextTokens);
                    MysqlCommandHelper.AddDate(cmd, "@created_utc", profile.CreatedUtc);
                    BindMutable(cmd, profile);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return profile;
        }

        /// <inheritdoc />
        public async Task<ModelProfile?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<ModelProfile> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM model_profiles WHERE id = @id;",
                cmd => MysqlCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<ModelProfile?> ReadByIdentityKeyAsync(string identityKey, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(identityKey)) throw new ArgumentNullException(nameof(identityKey));
            List<ModelProfile> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM model_profiles WHERE identity_key = @identity_key;",
                cmd => MysqlCommandHelper.Add(cmd, "@identity_key", identityKey), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<ModelProfile> UpdateAsync(ModelProfile profile, CancellationToken token = default)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            profile.LastUpdateUtc = DateTime.UtcNow;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _UpdateProfile, cmd => BindMutable(cmd, profile), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return profile;
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

            Action<MySqlCommand> bind = cmd =>
            {
                if (query.TenantId != null) MysqlCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (query.Runtime != null) MysqlCommandHelper.Add(cmd, "@runtime", query.Runtime.ToLowerInvariant());
                if (query.Provider != null) MysqlCommandHelper.Add(cmd, "@provider", query.Provider.ToLowerInvariant());
                if (query.ModelContains != null) MysqlCommandHelper.Add(cmd, "@model", VesselHealthQueryBuilder.ContainsPattern(query.ModelContains));
            };

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await MysqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM model_profiles" + where + ";", bind, token).ConfigureAwait(false);
                List<ModelProfile> rows = await MysqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM model_profiles" + where + " ORDER BY created_utc DESC, id DESC" + MysqlCommandHelper.Page(offset, pageSize) + ";",
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
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _InsertRevision, cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@id", revision.Id);
                    MysqlCommandHelper.Add(cmd, "@profile_id", revision.ProfileId);
                    MysqlCommandHelper.Add(cmd, "@tenant_id", revision.TenantId);
                    MysqlCommandHelper.Add(cmd, "@runtime_version", revision.RuntimeVersion);
                    MysqlCommandHelper.Add(cmd, "@model_digest", revision.ModelDigest);
                    MysqlCommandHelper.Add(cmd, "@sampling_temperature", revision.Sampling.Temperature);
                    MysqlCommandHelper.Add(cmd, "@sampling_top_p", revision.Sampling.TopP);
                    MysqlCommandHelper.Add(cmd, "@tool_tools", revision.ToolConfiguration.Tools);
                    MysqlCommandHelper.Add(cmd, "@tool_mcp", revision.ToolConfiguration.Mcp);
                    MysqlCommandHelper.AddDate(cmd, "@first_seen_utc", revision.FirstSeenUtc);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            revision.CompatibleWith = new List<string>();
            return revision;
        }

        /// <inheritdoc />
        public async Task<ModelProfileRevision?> ReadRevisionAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                List<ModelProfileRevision> rows = await MysqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM model_profile_revisions WHERE id = @id;",
                    cmd => MysqlCommandHelper.Add(cmd, "@id", id), RevisionFromReader, token).ConfigureAwait(false);
                ModelProfileRevision? revision = rows.FirstOrDefault();
                if (revision == null) return null;
                List<string[]> marks = await MysqlCommandHelper.QueryAsync(conn,
                    "SELECT revision_id, compatible_revision_id FROM model_profile_revision_compat WHERE revision_id = @id ORDER BY compatible_revision_id;",
                    cmd => MysqlCommandHelper.Add(cmd, "@id", id), CompatFromReader, token).ConfigureAwait(false);
                revision.CompatibleWith = marks.Select(m => m[1]).ToList();
                return revision;
            }
        }

        /// <inheritdoc />
        public async Task<List<ModelProfileRevision>> ReadRevisionsAsync(string profileId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(profileId)) throw new ArgumentNullException(nameof(profileId));
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                List<ModelProfileRevision> rows = await MysqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM model_profile_revisions WHERE profile_id = @profile_id ORDER BY first_seen_utc ASC, id ASC;",
                    cmd => MysqlCommandHelper.Add(cmd, "@profile_id", profileId), RevisionFromReader, token).ConfigureAwait(false);
                if (rows.Count == 0) return rows;
                List<string[]> marks = await MysqlCommandHelper.QueryAsync(conn,
                    "SELECT c.revision_id, c.compatible_revision_id FROM model_profile_revision_compat c "
                    + "INNER JOIN model_profile_revisions r ON r.id = c.revision_id WHERE r.profile_id = @profile_id "
                    + "ORDER BY c.revision_id, c.compatible_revision_id;",
                    cmd => MysqlCommandHelper.Add(cmd, "@profile_id", profileId), CompatFromReader, token).ConfigureAwait(false);
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
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx,
                    "DELETE FROM model_profile_revision_compat WHERE revision_id = @id OR compatible_revision_id = @id;",
                    cmd => MysqlCommandHelper.Add(cmd, "@id", revisionId), token).ConfigureAwait(false);
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
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                updated = await MysqlCommandHelper.ExecuteAsync(conn, tx, "UPDATE captains SET model_profile_id = @profile_id WHERE id = @id;", cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@id", captainId);
                    MysqlCommandHelper.Add(cmd, "@profile_id", String.IsNullOrEmpty(profileId) ? null : profileId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return updated > 0;
        }

        #endregion

        #region Private-Methods

        private static async Task InsertMarkAsync(MySqlConnection conn, MySqlTransaction tx, string revisionId, string otherId, DateTime now, CancellationToken token)
        {
            await MysqlCommandHelper.ExecuteAsync(conn, tx,
                "INSERT INTO model_profile_revision_compat (revision_id, compatible_revision_id, created_utc) VALUES (@revision_id, @compatible_revision_id, @created_utc);",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@revision_id", revisionId);
                    MysqlCommandHelper.Add(cmd, "@compatible_revision_id", otherId);
                    MysqlCommandHelper.AddDate(cmd, "@created_utc", now);
                }, token).ConfigureAwait(false);
        }

        private static void BindMutable(MySqlCommand cmd, ModelProfile profile)
        {
            MysqlCommandHelper.Add(cmd, "@id", profile.Id);
            MysqlCommandHelper.Add(cmd, "@tools_available", profile.Capabilities.ToolsAvailable);
            MysqlCommandHelper.Add(cmd, "@mcp_available", profile.Capabilities.McpAvailable);
            MysqlCommandHelper.Add(cmd, "@rendering_template", profile.Rendering.Template);
            MysqlCommandHelper.Add(cmd, "@rendering_repeat_contract_at_end", profile.Rendering.RepeatContractAtEnd);
            MysqlCommandHelper.Add(cmd, "@rendering_max_files_per_mission", profile.Rendering.MaxFilesPerMission);
            MysqlCommandHelper.Add(cmd, "@notes", profile.Notes);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", profile.LastUpdateUtc);
        }

        private static ModelProfile FromReader(MySqlDataReader reader)
        {
            ModelProfile profile = new ModelProfile();
            profile.Id = reader["id"].ToString()!;
            profile.TenantId = MysqlCommandHelper.ReadString(reader["tenant_id"]);
            profile.Identity.Runtime = MysqlCommandHelper.ReadString(reader["runtime"]) ?? String.Empty;
            profile.Identity.Provider = MysqlCommandHelper.ReadString(reader["provider"]);
            profile.Identity.Model = MysqlCommandHelper.ReadString(reader["model"]);
            profile.Identity.ServedContextTokens = MysqlCommandHelper.ReadNullableInt(reader["served_context_tokens"]);
            profile.Capabilities.ToolsAvailable = MysqlCommandHelper.ReadNullableBool(reader["tools_available"]);
            profile.Capabilities.McpAvailable = MysqlCommandHelper.ReadNullableBool(reader["mcp_available"]);
            profile.Rendering.Template = MysqlCommandHelper.ReadString(reader["rendering_template"]);
            profile.Rendering.RepeatContractAtEnd = MysqlCommandHelper.ReadNullableBool(reader["rendering_repeat_contract_at_end"]);
            int? maxFiles = MysqlCommandHelper.ReadNullableInt(reader["rendering_max_files_per_mission"]);
            profile.Rendering.MaxFilesPerMission = maxFiles.HasValue && maxFiles.Value >= 1 ? maxFiles : null;
            profile.Notes = MysqlCommandHelper.ReadString(reader["notes"]);
            profile.CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]);
            profile.LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return profile;
        }

        private static ModelProfileRevision RevisionFromReader(MySqlDataReader reader)
        {
            ModelProfileRevision revision = new ModelProfileRevision();
            revision.Id = reader["id"].ToString()!;
            revision.ProfileId = reader["profile_id"].ToString()!;
            revision.TenantId = MysqlCommandHelper.ReadString(reader["tenant_id"]);
            revision.RuntimeVersion = MysqlCommandHelper.ReadString(reader["runtime_version"]);
            revision.ModelDigest = MysqlCommandHelper.ReadString(reader["model_digest"]);
            revision.Sampling.Temperature = MysqlCommandHelper.ReadNullableDouble(reader["sampling_temperature"]);
            revision.Sampling.TopP = MysqlCommandHelper.ReadNullableDouble(reader["sampling_top_p"]);
            revision.ToolConfiguration.Tools = MysqlCommandHelper.ReadNullableBool(reader["tool_tools"]);
            revision.ToolConfiguration.Mcp = MysqlCommandHelper.ReadNullableBool(reader["tool_mcp"]);
            revision.FirstSeenUtc = MysqlCommandHelper.ReadDate(reader["first_seen_utc"]);
            return revision;
        }

        private static string[] CompatFromReader(MySqlDataReader reader)
        {
            return new string[] { reader["revision_id"].ToString()!, reader["compatible_revision_id"].ToString()! };
        }

        #endregion
    }
}
