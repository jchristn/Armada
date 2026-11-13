namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of release persistence.
    /// </summary>
    public class ReleaseMethods : IReleaseMethods
    {
        private readonly string _ConnectionString;
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ReleaseMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        /// <inheritdoc />
        public async Task<Release> CreateAsync(Release release, CancellationToken token = default)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));
            release.LastUpdateUtc = DateTime.UtcNow;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO releases
                        (id, tenant_id, user_id, vessel_id, workflow_profile_id, title, version, tag_name, summary, notes, status,
                         voyage_ids_json, mission_ids_json, check_run_ids_json, artifacts_json, created_utc, last_update_utc, published_utc)
                        VALUES
                        (@id, @tenant_id, @user_id, @vessel_id, @workflow_profile_id, @title, @version, @tag_name, @summary, @notes, @status,
                         @voyage_ids_json, @mission_ids_json, @check_run_ids_json, @artifacts_json, @created_utc, @last_update_utc, @published_utc);";
                    AddParameters(cmd, release);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return release;
        }

        /// <inheritdoc />
        public async Task<Release?> ReadAsync(string id, ReleaseQuery? query = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    List<string> conditions = new List<string> { "id = @id" };
                    List<MySqlParameter> parameters = new List<MySqlParameter> { new MySqlParameter("@id", id) };
                    ApplyQueryFilters(query, conditions, parameters);
                    cmd.CommandText = "SELECT * FROM releases WHERE " + String.Join(" AND ", conditions) + " LIMIT 1;";
                    foreach (MySqlParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return FromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<Release> UpdateAsync(Release release, CancellationToken token = default)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));
            release.LastUpdateUtc = DateTime.UtcNow;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
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
                }
            }

            return release;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, ReleaseQuery? query = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    List<string> conditions = new List<string> { "id = @id" };
                    List<MySqlParameter> parameters = new List<MySqlParameter> { new MySqlParameter("@id", id) };
                    ApplyQueryFilters(query, conditions, parameters);
                    cmd.CommandText = "DELETE FROM releases WHERE " + String.Join(" AND ", conditions) + ";";
                    foreach (MySqlParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Release>> EnumerateAsync(ReleaseQuery query, CancellationToken token = default)
        {
            query ??= new ReleaseQuery();
            int pageSize = query.PageSize <= 0 ? 25 : query.PageSize;
            int offset = query.PageNumber <= 1 ? 0 : (query.PageNumber - 1) * pageSize;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<MySqlParameter> parameters = new List<MySqlParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

                long totalCount;
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM releases" + whereClause + ";";
                    foreach (MySqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    totalCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                List<Release> results = new List<Release>();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM releases" + whereClause
                        + " ORDER BY COALESCE(published_utc, last_update_utc) DESC, created_utc DESC, id DESC LIMIT " + pageSize + " OFFSET " + offset + ";";
                    foreach (MySqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(FromReader(reader));
                    }
                }

                return new EnumerationResult<Release>
                {
                    PageNumber = query.PageNumber,
                    PageSize = pageSize,
                    TotalRecords = totalCount,
                    TotalPages = pageSize > 0 ? (int)Math.Ceiling((double)totalCount / pageSize) : 0,
                    Objects = results
                };
            }
        }

        /// <inheritdoc />
        public async Task<List<Release>> EnumerateAllAsync(ReleaseQuery query, CancellationToken token = default)
        {
            query ??= new ReleaseQuery();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<MySqlParameter> parameters = new List<MySqlParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

                List<Release> results = new List<Release>();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM releases" + whereClause + " ORDER BY COALESCE(published_utc, last_update_utc) DESC, created_utc DESC, id DESC;";
                    foreach (MySqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(FromReader(reader));
                    }
                }

                return results;
            }
        }

        private static void ApplyQueryFilters(ReleaseQuery? query, List<string> conditions, List<MySqlParameter> parameters)
        {
            if (query == null) return;

            if (!String.IsNullOrWhiteSpace(query.TenantId))
            {
                conditions.Add("tenant_id = @tenant_id");
                parameters.Add(new MySqlParameter("@tenant_id", query.TenantId));
            }
            if (!String.IsNullOrWhiteSpace(query.UserId))
            {
                conditions.Add("user_id = @user_id");
                parameters.Add(new MySqlParameter("@user_id", query.UserId));
            }
            if (!String.IsNullOrWhiteSpace(query.VesselId))
            {
                conditions.Add("vessel_id = @vessel_id");
                parameters.Add(new MySqlParameter("@vessel_id", query.VesselId));
            }
            if (!String.IsNullOrWhiteSpace(query.WorkflowProfileId))
            {
                conditions.Add("workflow_profile_id = @workflow_profile_id");
                parameters.Add(new MySqlParameter("@workflow_profile_id", query.WorkflowProfileId));
            }
            if (!String.IsNullOrWhiteSpace(query.VoyageId))
            {
                conditions.Add("voyage_ids_json LIKE @voyage_like");
                parameters.Add(new MySqlParameter("@voyage_like", "%\"" + query.VoyageId + "\"%"));
            }
            if (!String.IsNullOrWhiteSpace(query.MissionId))
            {
                conditions.Add("mission_ids_json LIKE @mission_like");
                parameters.Add(new MySqlParameter("@mission_like", "%\"" + query.MissionId + "\"%"));
            }
            if (!String.IsNullOrWhiteSpace(query.CheckRunId))
            {
                conditions.Add("check_run_ids_json LIKE @check_run_like");
                parameters.Add(new MySqlParameter("@check_run_like", "%\"" + query.CheckRunId + "\"%"));
            }
            if (query.Status.HasValue)
            {
                conditions.Add("status = @status");
                parameters.Add(new MySqlParameter("@status", query.Status.Value.ToString()));
            }
            if (!String.IsNullOrWhiteSpace(query.Search))
            {
                conditions.Add("(LOWER(title) LIKE @search OR LOWER(COALESCE(version, '')) LIKE @search OR LOWER(COALESCE(tag_name, '')) LIKE @search OR LOWER(COALESCE(summary, '')) LIKE @search OR LOWER(COALESCE(notes, '')) LIKE @search)");
                parameters.Add(new MySqlParameter("@search", "%" + query.Search.ToLowerInvariant() + "%"));
            }
            if (query.FromUtc.HasValue)
            {
                conditions.Add("created_utc >= @from_utc");
                parameters.Add(new MySqlParameter("@from_utc", query.FromUtc.Value));
            }
            if (query.ToUtc.HasValue)
            {
                conditions.Add("created_utc <= @to_utc");
                parameters.Add(new MySqlParameter("@to_utc", query.ToUtc.Value));
            }
        }

        private static void AddParameters(MySqlCommand cmd, Release release)
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
            cmd.Parameters.AddWithValue("@created_utc", release.CreatedUtc);
            cmd.Parameters.AddWithValue("@last_update_utc", release.LastUpdateUtc);
            cmd.Parameters.AddWithValue("@published_utc", (object?)release.PublishedUtc ?? DBNull.Value);
        }

        private static Release FromReader(MySqlDataReader reader)
        {
            Release release = new Release
            {
                Id = reader["id"].ToString() ?? String.Empty,
                TenantId = MysqlDatabaseDriver.NullableString(reader["tenant_id"]),
                UserId = MysqlDatabaseDriver.NullableString(reader["user_id"]),
                VesselId = MysqlDatabaseDriver.NullableString(reader["vessel_id"]),
                WorkflowProfileId = MysqlDatabaseDriver.NullableString(reader["workflow_profile_id"]),
                Title = reader["title"].ToString() ?? String.Empty,
                Version = MysqlDatabaseDriver.NullableString(reader["version"]),
                TagName = MysqlDatabaseDriver.NullableString(reader["tag_name"]),
                Summary = MysqlDatabaseDriver.NullableString(reader["summary"]),
                Notes = MysqlDatabaseDriver.NullableString(reader["notes"]),
                CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc),
                LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc),
                PublishedUtc = reader["published_utc"] == DBNull.Value ? null : DateTime.SpecifyKind(Convert.ToDateTime(reader["published_utc"]), DateTimeKind.Utc)
            };

            if (Enum.TryParse(reader["status"].ToString(), true, out ReleaseStatusEnum status))
                release.Status = status;

            release.VoyageIds = Deserialize<List<string>>(MysqlDatabaseDriver.NullableString(reader["voyage_ids_json"])) ?? new List<string>();
            release.MissionIds = Deserialize<List<string>>(MysqlDatabaseDriver.NullableString(reader["mission_ids_json"])) ?? new List<string>();
            release.CheckRunIds = Deserialize<List<string>>(MysqlDatabaseDriver.NullableString(reader["check_run_ids_json"])) ?? new List<string>();
            release.Artifacts = Deserialize<List<ReleaseArtifact>>(MysqlDatabaseDriver.NullableString(reader["artifacts_json"])) ?? new List<ReleaseArtifact>();
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

        private static MySqlParameter CloneParameter(MySqlParameter parameter)
        {
            return new MySqlParameter(parameter.ParameterName, parameter.Value ?? DBNull.Value);
        }
    }
}
