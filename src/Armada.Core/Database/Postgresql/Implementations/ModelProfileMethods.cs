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
    /// PostgreSQL implementation of model profile persistence.
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
        /// <param name="driver">PostgreSQL database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public ModelProfileMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<ModelProfile> CreateAsync(ModelProfile profile, string identityKey, CancellationToken token = default)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (String.IsNullOrEmpty(identityKey)) throw new ArgumentNullException(nameof(identityKey));
            profile.LastUpdateUtc = DateTime.UtcNow;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _InsertProfile, cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", profile.TenantId);
                    PostgresqlCommandHelper.Add(cmd, "@identity_key", identityKey);
                    PostgresqlCommandHelper.Add(cmd, "@runtime", profile.Identity.Runtime ?? String.Empty);
                    PostgresqlCommandHelper.Add(cmd, "@provider", profile.Identity.Provider);
                    PostgresqlCommandHelper.Add(cmd, "@model", profile.Identity.Model);
                    PostgresqlCommandHelper.Add(cmd, "@served_context_tokens", profile.Identity.ServedContextTokens);
                    PostgresqlCommandHelper.AddDate(cmd, "@created_utc", profile.CreatedUtc);
                    BindMutable(cmd, profile);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return profile;
        }

        /// <inheritdoc />
        public async Task<ModelProfile?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<ModelProfile> rows = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM model_profiles WHERE id = @id;",
                cmd => PostgresqlCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<ModelProfile?> ReadByIdentityKeyAsync(string identityKey, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(identityKey)) throw new ArgumentNullException(nameof(identityKey));
            List<ModelProfile> rows = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM model_profiles WHERE identity_key = @identity_key;",
                cmd => PostgresqlCommandHelper.Add(cmd, "@identity_key", identityKey), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<ModelProfile> UpdateAsync(ModelProfile profile, CancellationToken token = default)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            profile.LastUpdateUtc = DateTime.UtcNow;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _UpdateProfile, cmd => BindMutable(cmd, profile), token).ConfigureAwait(false);
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

            Action<NpgsqlCommand> bind = cmd =>
            {
                if (query.TenantId != null) PostgresqlCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (query.Runtime != null) PostgresqlCommandHelper.Add(cmd, "@runtime", query.Runtime.ToLowerInvariant());
                if (query.Provider != null) PostgresqlCommandHelper.Add(cmd, "@provider", query.Provider.ToLowerInvariant());
                if (query.ModelContains != null) PostgresqlCommandHelper.Add(cmd, "@model", VesselHealthQueryBuilder.ContainsPattern(query.ModelContains));
            };

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (NpgsqlConnection conn = new NpgsqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await PostgresqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM model_profiles" + where + ";", bind, token).ConfigureAwait(false);
                List<ModelProfile> rows = await PostgresqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM model_profiles" + where + " ORDER BY created_utc DESC, id DESC" + PostgresqlCommandHelper.Page(offset, pageSize) + ";",
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
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _InsertRevision, cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@id", revision.Id);
                    PostgresqlCommandHelper.Add(cmd, "@profile_id", revision.ProfileId);
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", revision.TenantId);
                    PostgresqlCommandHelper.Add(cmd, "@runtime_version", revision.RuntimeVersion);
                    PostgresqlCommandHelper.Add(cmd, "@model_digest", revision.ModelDigest);
                    PostgresqlCommandHelper.Add(cmd, "@sampling_temperature", revision.Sampling.Temperature);
                    PostgresqlCommandHelper.Add(cmd, "@sampling_top_p", revision.Sampling.TopP);
                    PostgresqlCommandHelper.Add(cmd, "@tool_tools", revision.ToolConfiguration.Tools);
                    PostgresqlCommandHelper.Add(cmd, "@tool_mcp", revision.ToolConfiguration.Mcp);
                    PostgresqlCommandHelper.AddDate(cmd, "@first_seen_utc", revision.FirstSeenUtc);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            revision.CompatibleWith = new List<string>();
            return revision;
        }

        /// <inheritdoc />
        public async Task<ModelProfileRevision?> ReadRevisionAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            using (NpgsqlConnection conn = new NpgsqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                List<ModelProfileRevision> rows = await PostgresqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM model_profile_revisions WHERE id = @id;",
                    cmd => PostgresqlCommandHelper.Add(cmd, "@id", id), RevisionFromReader, token).ConfigureAwait(false);
                ModelProfileRevision? revision = rows.FirstOrDefault();
                if (revision == null) return null;
                List<string[]> marks = await PostgresqlCommandHelper.QueryAsync(conn,
                    "SELECT revision_id, compatible_revision_id FROM model_profile_revision_compat WHERE revision_id = @id ORDER BY compatible_revision_id;",
                    cmd => PostgresqlCommandHelper.Add(cmd, "@id", id), CompatFromReader, token).ConfigureAwait(false);
                revision.CompatibleWith = marks.Select(m => m[1]).ToList();
                return revision;
            }
        }

        /// <inheritdoc />
        public async Task<List<ModelProfileRevision>> ReadRevisionsAsync(string profileId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(profileId)) throw new ArgumentNullException(nameof(profileId));
            using (NpgsqlConnection conn = new NpgsqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                List<ModelProfileRevision> rows = await PostgresqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM model_profile_revisions WHERE profile_id = @profile_id ORDER BY first_seen_utc ASC, id ASC;",
                    cmd => PostgresqlCommandHelper.Add(cmd, "@profile_id", profileId), RevisionFromReader, token).ConfigureAwait(false);
                if (rows.Count == 0) return rows;
                List<string[]> marks = await PostgresqlCommandHelper.QueryAsync(conn,
                    "SELECT c.revision_id, c.compatible_revision_id FROM model_profile_revision_compat c "
                    + "INNER JOIN model_profile_revisions r ON r.id = c.revision_id WHERE r.profile_id = @profile_id "
                    + "ORDER BY c.revision_id, c.compatible_revision_id;",
                    cmd => PostgresqlCommandHelper.Add(cmd, "@profile_id", profileId), CompatFromReader, token).ConfigureAwait(false);
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
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx,
                    "DELETE FROM model_profile_revision_compat WHERE revision_id = @id OR compatible_revision_id = @id;",
                    cmd => PostgresqlCommandHelper.Add(cmd, "@id", revisionId), token).ConfigureAwait(false);
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
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                updated = await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "UPDATE captains SET model_profile_id = @profile_id WHERE id = @id;", cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@id", captainId);
                    PostgresqlCommandHelper.Add(cmd, "@profile_id", String.IsNullOrEmpty(profileId) ? null : profileId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return updated > 0;
        }

        #endregion

        #region Private-Methods

        private static async Task InsertMarkAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string revisionId, string otherId, DateTime now, CancellationToken token)
        {
            await PostgresqlCommandHelper.ExecuteAsync(conn, tx,
                "INSERT INTO model_profile_revision_compat (revision_id, compatible_revision_id, created_utc) VALUES (@revision_id, @compatible_revision_id, @created_utc);",
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@revision_id", revisionId);
                    PostgresqlCommandHelper.Add(cmd, "@compatible_revision_id", otherId);
                    PostgresqlCommandHelper.AddDate(cmd, "@created_utc", now);
                }, token).ConfigureAwait(false);
        }

        private static void BindMutable(NpgsqlCommand cmd, ModelProfile profile)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", profile.Id);
            PostgresqlCommandHelper.Add(cmd, "@tools_available", profile.Capabilities.ToolsAvailable);
            PostgresqlCommandHelper.Add(cmd, "@mcp_available", profile.Capabilities.McpAvailable);
            PostgresqlCommandHelper.Add(cmd, "@rendering_template", profile.Rendering.Template);
            PostgresqlCommandHelper.Add(cmd, "@rendering_repeat_contract_at_end", profile.Rendering.RepeatContractAtEnd);
            PostgresqlCommandHelper.Add(cmd, "@rendering_max_files_per_mission", profile.Rendering.MaxFilesPerMission);
            PostgresqlCommandHelper.Add(cmd, "@notes", profile.Notes);
            PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", profile.LastUpdateUtc);
        }

        private static ModelProfile FromReader(NpgsqlDataReader reader)
        {
            ModelProfile profile = new ModelProfile();
            profile.Id = reader["id"].ToString()!;
            profile.TenantId = PostgresqlCommandHelper.ReadString(reader["tenant_id"]);
            profile.Identity.Runtime = PostgresqlCommandHelper.ReadString(reader["runtime"]) ?? String.Empty;
            profile.Identity.Provider = PostgresqlCommandHelper.ReadString(reader["provider"]);
            profile.Identity.Model = PostgresqlCommandHelper.ReadString(reader["model"]);
            profile.Identity.ServedContextTokens = PostgresqlCommandHelper.ReadNullableInt(reader["served_context_tokens"]);
            profile.Capabilities.ToolsAvailable = PostgresqlCommandHelper.ReadNullableBool(reader["tools_available"]);
            profile.Capabilities.McpAvailable = PostgresqlCommandHelper.ReadNullableBool(reader["mcp_available"]);
            profile.Rendering.Template = PostgresqlCommandHelper.ReadString(reader["rendering_template"]);
            profile.Rendering.RepeatContractAtEnd = PostgresqlCommandHelper.ReadNullableBool(reader["rendering_repeat_contract_at_end"]);
            int? maxFiles = PostgresqlCommandHelper.ReadNullableInt(reader["rendering_max_files_per_mission"]);
            profile.Rendering.MaxFilesPerMission = maxFiles.HasValue && maxFiles.Value >= 1 ? maxFiles : null;
            profile.Notes = PostgresqlCommandHelper.ReadString(reader["notes"]);
            profile.CreatedUtc = PostgresqlCommandHelper.ReadDate(reader["created_utc"]);
            profile.LastUpdateUtc = PostgresqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return profile;
        }

        private static ModelProfileRevision RevisionFromReader(NpgsqlDataReader reader)
        {
            ModelProfileRevision revision = new ModelProfileRevision();
            revision.Id = reader["id"].ToString()!;
            revision.ProfileId = reader["profile_id"].ToString()!;
            revision.TenantId = PostgresqlCommandHelper.ReadString(reader["tenant_id"]);
            revision.RuntimeVersion = PostgresqlCommandHelper.ReadString(reader["runtime_version"]);
            revision.ModelDigest = PostgresqlCommandHelper.ReadString(reader["model_digest"]);
            revision.Sampling.Temperature = PostgresqlCommandHelper.ReadNullableDouble(reader["sampling_temperature"]);
            revision.Sampling.TopP = PostgresqlCommandHelper.ReadNullableDouble(reader["sampling_top_p"]);
            revision.ToolConfiguration.Tools = PostgresqlCommandHelper.ReadNullableBool(reader["tool_tools"]);
            revision.ToolConfiguration.Mcp = PostgresqlCommandHelper.ReadNullableBool(reader["tool_mcp"]);
            revision.FirstSeenUtc = PostgresqlCommandHelper.ReadDate(reader["first_seen_utc"]);
            return revision;
        }

        private static string[] CompatFromReader(NpgsqlDataReader reader)
        {
            return new string[] { reader["revision_id"].ToString()!, reader["compatible_revision_id"].ToString()! };
        }

        #endregion
    }
}
