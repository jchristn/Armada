namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// PostgreSQL implementation of release persistence.
    /// </summary>
    public class ReleaseMethods : IReleaseMethods
    {
        private readonly PostgresqlDatabaseDriver _Driver;
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ReleaseMethods(PostgresqlDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc />
        public async Task<Release> CreateAsync(Release release, CancellationToken token = default)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));
            release.LastUpdateUtc = DateTime.UtcNow;

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
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

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    List<string> conditions = new List<string> { "id = @id" };
                    List<NpgsqlParameter> parameters = new List<NpgsqlParameter> { new NpgsqlParameter("@id", id) };
                    ApplyQueryFilters(query, conditions, parameters);
                    cmd.CommandText = "SELECT * FROM releases WHERE " + String.Join(" AND ", conditions) + " LIMIT 1;";
                    foreach (NpgsqlParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
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

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    List<string> conditions = new List<string> { "id = @id" };
                    List<NpgsqlParameter> parameters = new List<NpgsqlParameter> { new NpgsqlParameter("@id", id) };
                    ApplyQueryFilters(query, conditions, parameters);
                    cmd.CommandText = "DELETE FROM releases WHERE " + String.Join(" AND ", conditions) + ";";
                    foreach (NpgsqlParameter parameter in parameters) cmd.Parameters.Add(parameter);
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

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<NpgsqlParameter> parameters = new List<NpgsqlParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

                long totalCount;
                using (NpgsqlCommand countCmd = conn.CreateCommand())
                {
                    countCmd.CommandText = "SELECT COUNT(*) FROM releases" + whereClause + ";";
                    foreach (NpgsqlParameter parameter in parameters) countCmd.Parameters.Add(CloneParameter(parameter));
                    totalCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                List<Release> results = new List<Release>();
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM releases" + whereClause
                        + " ORDER BY COALESCE(published_utc, last_update_utc) DESC, created_utc DESC, id DESC LIMIT @page_size OFFSET @offset;";
                    foreach (NpgsqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    cmd.Parameters.AddWithValue("@page_size", pageSize);
                    cmd.Parameters.AddWithValue("@offset", offset);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<NpgsqlParameter> parameters = new List<NpgsqlParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

                List<Release> results = new List<Release>();
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM releases" + whereClause + " ORDER BY COALESCE(published_utc, last_update_utc) DESC, created_utc DESC, id DESC;";
                    foreach (NpgsqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(FromReader(reader));
                    }
                }

                return results;
            }
        }

        private static void ApplyQueryFilters(ReleaseQuery? query, List<string> conditions, List<NpgsqlParameter> parameters)
        {
            if (query == null) return;

            if (!String.IsNullOrWhiteSpace(query.TenantId))
            {
                conditions.Add("tenant_id = @tenant_id");
                parameters.Add(new NpgsqlParameter("@tenant_id", query.TenantId));
            }
            if (!String.IsNullOrWhiteSpace(query.UserId))
            {
                conditions.Add("user_id = @user_id");
                parameters.Add(new NpgsqlParameter("@user_id", query.UserId));
            }
            if (!String.IsNullOrWhiteSpace(query.VesselId))
            {
                conditions.Add("vessel_id = @vessel_id");
                parameters.Add(new NpgsqlParameter("@vessel_id", query.VesselId));
            }
            if (!String.IsNullOrWhiteSpace(query.WorkflowProfileId))
            {
                conditions.Add("workflow_profile_id = @workflow_profile_id");
                parameters.Add(new NpgsqlParameter("@workflow_profile_id", query.WorkflowProfileId));
            }
            if (!String.IsNullOrWhiteSpace(query.VoyageId))
            {
                conditions.Add("voyage_ids_json LIKE @voyage_like");
                parameters.Add(new NpgsqlParameter("@voyage_like", "%\"" + query.VoyageId + "\"%"));
            }
            if (!String.IsNullOrWhiteSpace(query.MissionId))
            {
                conditions.Add("mission_ids_json LIKE @mission_like");
                parameters.Add(new NpgsqlParameter("@mission_like", "%\"" + query.MissionId + "\"%"));
            }
            if (!String.IsNullOrWhiteSpace(query.CheckRunId))
            {
                conditions.Add("check_run_ids_json LIKE @check_run_like");
                parameters.Add(new NpgsqlParameter("@check_run_like", "%\"" + query.CheckRunId + "\"%"));
            }
            if (query.Status.HasValue)
            {
                conditions.Add("status = @status");
                parameters.Add(new NpgsqlParameter("@status", query.Status.Value.ToString()));
            }
            if (!String.IsNullOrWhiteSpace(query.Search))
            {
                conditions.Add("(LOWER(title) LIKE @search OR LOWER(COALESCE(version, '')) LIKE @search OR LOWER(COALESCE(tag_name, '')) LIKE @search OR LOWER(COALESCE(summary, '')) LIKE @search OR LOWER(COALESCE(notes, '')) LIKE @search)");
                parameters.Add(new NpgsqlParameter("@search", "%" + query.Search.ToLowerInvariant() + "%"));
            }
            if (query.FromUtc.HasValue)
            {
                conditions.Add("created_utc >= @from_utc");
                parameters.Add(PostgresqlCommandHelper.DateParameter("@from_utc", query.FromUtc.Value));
            }
            if (query.ToUtc.HasValue)
            {
                conditions.Add("created_utc <= @to_utc");
                parameters.Add(PostgresqlCommandHelper.DateParameter("@to_utc", query.ToUtc.Value));
            }
        }

        private static void AddParameters(NpgsqlCommand cmd, Release release)
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
            PostgresqlCommandHelper.AddDate(cmd, "@created_utc", release.CreatedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", release.LastUpdateUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@published_utc", release.PublishedUtc);
        }

        private static Release FromReader(NpgsqlDataReader reader)
        {
            Release release = new Release
            {
                Id = reader["id"].ToString() ?? String.Empty,
                TenantId = NullableString(reader["tenant_id"]),
                UserId = NullableString(reader["user_id"]),
                VesselId = NullableString(reader["vessel_id"]),
                WorkflowProfileId = NullableString(reader["workflow_profile_id"]),
                Title = reader["title"].ToString() ?? String.Empty,
                Version = NullableString(reader["version"]),
                TagName = NullableString(reader["tag_name"]),
                Summary = NullableString(reader["summary"]),
                Notes = NullableString(reader["notes"]),
                CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc),
                LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc),
                PublishedUtc = reader["published_utc"] == DBNull.Value ? null : DateTime.SpecifyKind(Convert.ToDateTime(reader["published_utc"]), DateTimeKind.Utc)
            };

            if (Enum.TryParse(reader["status"].ToString(), true, out ReleaseStatusEnum status))
                release.Status = status;

            release.VoyageIds = Deserialize<List<string>>(NullableString(reader["voyage_ids_json"])) ?? new List<string>();
            release.MissionIds = Deserialize<List<string>>(NullableString(reader["mission_ids_json"])) ?? new List<string>();
            release.CheckRunIds = Deserialize<List<string>>(NullableString(reader["check_run_ids_json"])) ?? new List<string>();
            release.Artifacts = Deserialize<List<ReleaseArtifact>>(NullableString(reader["artifacts_json"])) ?? new List<ReleaseArtifact>();
            return release;
        }

        private static string? NullableString(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string stringValue = value.ToString()!;
            return String.IsNullOrEmpty(stringValue) ? null : stringValue;
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

        private static NpgsqlParameter CloneParameter(NpgsqlParameter parameter)
        {
            return new NpgsqlParameter(parameter.ParameterName, parameter.Value ?? DBNull.Value);
        }
    }
}
