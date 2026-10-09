namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQLite implementation of release persistence.
    /// </summary>
    public class ReleaseMethods : IReleaseMethods
    {
        private readonly SqliteDatabaseDriver _Driver;
        private readonly DatabaseSettings _Settings;
        private readonly LoggingModule _Logging;
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ReleaseMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        /// <inheritdoc />
        public async Task<Release> CreateAsync(Release release, CancellationToken token = default)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));
            release.LastUpdateUtc = DateTime.UtcNow;

            using SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false);
            using SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO releases
                (id, tenant_id, user_id, vessel_id, workflow_profile_id, title, version, tag_name, summary, notes, status,
                 voyage_ids_json, mission_ids_json, check_run_ids_json, artifacts_json, created_utc, last_update_utc, published_utc)
                VALUES
                (@id, @tenant_id, @user_id, @vessel_id, @workflow_profile_id, @title, @version, @tag_name, @summary, @notes, @status,
                 @voyage_ids_json, @mission_ids_json, @check_run_ids_json, @artifacts_json, @created_utc, @last_update_utc, @published_utc);";
            AddParameters(cmd, release);
            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return release;
        }

        /// <inheritdoc />
        public async Task<Release?> ReadAsync(string id, ReleaseQuery? query = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            using SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);
            using SqliteCommand cmd = conn.CreateCommand();

            List<string> conditions = new List<string> { "id = @id" };
            List<SqliteParameter> parameters = new List<SqliteParameter> { new SqliteParameter("@id", id) };
            ApplyQueryFilters(query, conditions, parameters, false);
            cmd.CommandText = "SELECT * FROM releases WHERE " + String.Join(" AND ", conditions) + " LIMIT 1;";
            foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(parameter);

            using SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (await reader.ReadAsync(token).ConfigureAwait(false))
                return FromReader(reader);
            return null;
        }

        /// <inheritdoc />
        public async Task<Release> UpdateAsync(Release release, CancellationToken token = default)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));
            release.LastUpdateUtc = DateTime.UtcNow;

            using SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false);
            using SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE releases SET
                tenant_id = @tenant_id,
                user_id = @user_id,
                vessel_id = @vessel_id,
                workflow_profile_id = @workflow_profile_id,
                title = @title,
                version = @version,
                tag_name = @tag_name,
                summary = @summary,
                notes = @notes,
                status = @status,
                voyage_ids_json = @voyage_ids_json,
                mission_ids_json = @mission_ids_json,
                check_run_ids_json = @check_run_ids_json,
                artifacts_json = @artifacts_json,
                last_update_utc = @last_update_utc,
                published_utc = @published_utc
                WHERE id = @id;";
            AddParameters(cmd, release);
            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return release;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, ReleaseQuery? query = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            using SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false);
            using SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);
            using SqliteCommand cmd = conn.CreateCommand();
            List<string> conditions = new List<string> { "id = @id" };
            List<SqliteParameter> parameters = new List<SqliteParameter> { new SqliteParameter("@id", id) };
            ApplyQueryFilters(query, conditions, parameters, false);
            cmd.CommandText = "DELETE FROM releases WHERE " + String.Join(" AND ", conditions) + ";";
            foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(parameter);
            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Release>> EnumerateAsync(ReleaseQuery query, CancellationToken token = default)
        {
            query ??= new ReleaseQuery();

            using SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);

            List<string> conditions = new List<string>();
            List<SqliteParameter> parameters = new List<SqliteParameter>();
            ApplyQueryFilters(query, conditions, parameters, true);
            string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            long totalCount;
            using (SqliteCommand countCmd = conn.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM releases" + whereClause + ";";
                foreach (SqliteParameter parameter in parameters) countCmd.Parameters.Add(new SqliteParameter(parameter.ParameterName, parameter.Value));
                totalCount = (long)(await countCmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
            }

            List<Release> results = new List<Release>();
            using (SqliteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM releases" + whereClause
                    + " ORDER BY COALESCE(published_utc, last_update_utc) DESC, created_utc DESC"
                    + " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(new SqliteParameter(parameter.ParameterName, parameter.Value));
                using SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                    results.Add(FromReader(reader));
            }

            return new EnumerationResult<Release>
            {
                PageNumber = query.PageNumber,
                PageSize = query.PageSize,
                TotalRecords = totalCount,
                TotalPages = query.PageSize > 0 ? (int)Math.Ceiling((double)totalCount / query.PageSize) : 0,
                Objects = results
            };
        }

        /// <inheritdoc />
        public async Task<List<Release>> EnumerateAllAsync(ReleaseQuery query, CancellationToken token = default)
        {
            query ??= new ReleaseQuery();

            using SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);
            using SqliteCommand cmd = conn.CreateCommand();

            List<string> conditions = new List<string>();
            List<SqliteParameter> parameters = new List<SqliteParameter>();
            ApplyQueryFilters(query, conditions, parameters, false);
            string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;
            cmd.CommandText = "SELECT * FROM releases" + whereClause + " ORDER BY COALESCE(published_utc, last_update_utc) DESC, created_utc DESC;";
            foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(parameter);

            List<Release> results = new List<Release>();
            using SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                results.Add(FromReader(reader));
            return results;
        }

        private static void ApplyQueryFilters(
            ReleaseQuery? query,
            List<string> conditions,
            List<SqliteParameter> parameters,
            bool includePaging)
        {
            if (query == null) return;

            if (!String.IsNullOrWhiteSpace(query.TenantId))
            {
                conditions.Add("tenant_id = @tenant_id");
                parameters.Add(new SqliteParameter("@tenant_id", query.TenantId));
            }
            if (!String.IsNullOrWhiteSpace(query.UserId))
            {
                conditions.Add("user_id = @user_id");
                parameters.Add(new SqliteParameter("@user_id", query.UserId));
            }
            if (!String.IsNullOrWhiteSpace(query.VesselId))
            {
                conditions.Add("vessel_id = @vessel_id");
                parameters.Add(new SqliteParameter("@vessel_id", query.VesselId));
            }
            if (!String.IsNullOrWhiteSpace(query.WorkflowProfileId))
            {
                conditions.Add("workflow_profile_id = @workflow_profile_id");
                parameters.Add(new SqliteParameter("@workflow_profile_id", query.WorkflowProfileId));
            }
            if (!String.IsNullOrWhiteSpace(query.VoyageId))
            {
                conditions.Add("voyage_ids_json LIKE @voyage_like");
                parameters.Add(new SqliteParameter("@voyage_like", "%\"" + query.VoyageId + "\"%"));
            }
            if (!String.IsNullOrWhiteSpace(query.MissionId))
            {
                conditions.Add("mission_ids_json LIKE @mission_like");
                parameters.Add(new SqliteParameter("@mission_like", "%\"" + query.MissionId + "\"%"));
            }
            if (!String.IsNullOrWhiteSpace(query.CheckRunId))
            {
                conditions.Add("check_run_ids_json LIKE @check_run_like");
                parameters.Add(new SqliteParameter("@check_run_like", "%\"" + query.CheckRunId + "\"%"));
            }
            if (query.Status.HasValue)
            {
                conditions.Add("status = @status");
                parameters.Add(new SqliteParameter("@status", query.Status.Value.ToString()));
            }
            if (!String.IsNullOrWhiteSpace(query.Search))
            {
                conditions.Add("(LOWER(title) LIKE @search OR LOWER(COALESCE(version, '')) LIKE @search OR LOWER(COALESCE(tag_name, '')) LIKE @search OR LOWER(COALESCE(summary, '')) LIKE @search OR LOWER(COALESCE(notes, '')) LIKE @search)");
                parameters.Add(new SqliteParameter("@search", "%" + query.Search.ToLowerInvariant() + "%"));
            }
            if (query.FromUtc.HasValue)
            {
                conditions.Add("created_utc >= @from_utc");
                parameters.Add(new SqliteParameter("@from_utc", SqliteDatabaseDriver.ToIso8601(query.FromUtc.Value)));
            }
            if (query.ToUtc.HasValue)
            {
                conditions.Add("created_utc <= @to_utc");
                parameters.Add(new SqliteParameter("@to_utc", SqliteDatabaseDriver.ToIso8601(query.ToUtc.Value)));
            }
        }

        private static void AddParameters(SqliteCommand cmd, Release release)
        {
            cmd.Parameters.AddWithValue("@id", release.Id);
            cmd.Parameters.AddWithValue("@tenant_id", (object?)release.TenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user_id", (object?)release.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@vessel_id", (object?)release.VesselId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@workflow_profile_id", (object?)release.WorkflowProfileId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@title", release.Title);
            cmd.Parameters.AddWithValue("@version", (object?)release.Version ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tag_name", (object?)release.TagName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@summary", (object?)release.Summary ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@notes", (object?)release.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@status", release.Status.ToString());
            cmd.Parameters.AddWithValue("@voyage_ids_json", JsonSerializer.Serialize(release.VoyageIds ?? new List<string>(), _Json));
            cmd.Parameters.AddWithValue("@mission_ids_json", JsonSerializer.Serialize(release.MissionIds ?? new List<string>(), _Json));
            cmd.Parameters.AddWithValue("@check_run_ids_json", JsonSerializer.Serialize(release.CheckRunIds ?? new List<string>(), _Json));
            cmd.Parameters.AddWithValue("@artifacts_json", JsonSerializer.Serialize(release.Artifacts ?? new List<ReleaseArtifact>(), _Json));
            cmd.Parameters.AddWithValue("@created_utc", SqliteDatabaseDriver.ToIso8601(release.CreatedUtc));
            cmd.Parameters.AddWithValue("@last_update_utc", SqliteDatabaseDriver.ToIso8601(release.LastUpdateUtc));
            cmd.Parameters.AddWithValue("@published_utc", release.PublishedUtc.HasValue ? SqliteDatabaseDriver.ToIso8601(release.PublishedUtc.Value) : DBNull.Value);
        }

        private static Release FromReader(SqliteDataReader reader)
        {
            Release release = new Release
            {
                Id = reader["id"].ToString() ?? String.Empty,
                TenantId = SqliteDatabaseDriver.NullableString(reader["tenant_id"]),
                UserId = SqliteDatabaseDriver.NullableString(reader["user_id"]),
                VesselId = SqliteDatabaseDriver.NullableString(reader["vessel_id"]),
                WorkflowProfileId = SqliteDatabaseDriver.NullableString(reader["workflow_profile_id"]),
                Title = reader["title"].ToString() ?? String.Empty,
                Version = SqliteDatabaseDriver.NullableString(reader["version"]),
                TagName = SqliteDatabaseDriver.NullableString(reader["tag_name"]),
                Summary = SqliteDatabaseDriver.NullableString(reader["summary"]),
                Notes = SqliteDatabaseDriver.NullableString(reader["notes"]),
                CreatedUtc = SqliteDatabaseDriver.FromIso8601(reader["created_utc"].ToString()!),
                LastUpdateUtc = SqliteDatabaseDriver.FromIso8601(reader["last_update_utc"].ToString()!),
                PublishedUtc = SqliteDatabaseDriver.FromIso8601Nullable(reader["published_utc"])
            };

            if (Enum.TryParse(reader["status"].ToString(), true, out ReleaseStatusEnum status))
                release.Status = status;

            release.VoyageIds = Deserialize<List<string>>(SqliteDatabaseDriver.NullableString(reader["voyage_ids_json"])) ?? new List<string>();
            release.MissionIds = Deserialize<List<string>>(SqliteDatabaseDriver.NullableString(reader["mission_ids_json"])) ?? new List<string>();
            release.CheckRunIds = Deserialize<List<string>>(SqliteDatabaseDriver.NullableString(reader["check_run_ids_json"])) ?? new List<string>();
            release.Artifacts = Deserialize<List<ReleaseArtifact>>(SqliteDatabaseDriver.NullableString(reader["artifacts_json"])) ?? new List<ReleaseArtifact>();
            return release;
        }

        private static T? Deserialize<T>(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return default;
            try
            {
                return JsonSerializer.Deserialize<T>(json, _Json);
            }
            catch
            {
                return default;
            }
        }
    }
}
