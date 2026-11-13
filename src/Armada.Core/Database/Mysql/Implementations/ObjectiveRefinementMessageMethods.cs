namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL objective refinement transcript persistence.
    /// </summary>
    public class ObjectiveRefinementMessageMethods : IObjectiveRefinementMessageMethods
    {
        private readonly string _ConnectionString;

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectiveRefinementMessageMethods"/> class.
        /// </summary>
        public ObjectiveRefinementMessageMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        /// <inheritdoc />
        public async Task<ObjectiveRefinementMessage> CreateAsync(ObjectiveRefinementMessage message, CancellationToken token = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO objective_refinement_messages
                        (id, objective_refinement_session_id, objective_id, tenant_id, user_id, role, sequence, content, is_selected, created_utc, last_update_utc)
                        VALUES
                        (@id, @objective_refinement_session_id, @objective_id, @tenant_id, @user_id, @role, @sequence, @content, @is_selected, @created_utc, @last_update_utc);";
                    Bind(cmd, message);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return message;
        }

        /// <inheritdoc />
        public async Task<ObjectiveRefinementMessage> UpdateAsync(ObjectiveRefinementMessage message, CancellationToken token = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE objective_refinement_messages SET
                        objective_refinement_session_id = @objective_refinement_session_id,
                        objective_id = @objective_id,
                        tenant_id = @tenant_id,
                        user_id = @user_id,
                        role = @role,
                        sequence = @sequence,
                        content = @content,
                        is_selected = @is_selected,
                        created_utc = @created_utc,
                        last_update_utc = @last_update_utc
                        WHERE id = @id;";
                    Bind(cmd, message);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return message;
        }

        /// <inheritdoc />
        public async Task<ObjectiveRefinementMessage?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM objective_refinement_messages WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
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
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM objective_refinement_messages WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task DeleteBySessionAsync(string objectiveRefinementSessionId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(objectiveRefinementSessionId)) throw new ArgumentNullException(nameof(objectiveRefinementSessionId));
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM objective_refinement_messages WHERE objective_refinement_session_id = @session_id;";
                    cmd.Parameters.AddWithValue("@session_id", objectiveRefinementSessionId);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<List<ObjectiveRefinementMessage>> EnumerateBySessionAsync(string objectiveRefinementSessionId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(objectiveRefinementSessionId)) throw new ArgumentNullException(nameof(objectiveRefinementSessionId));
            return await EnumerateAsync(
                "SELECT * FROM objective_refinement_messages WHERE objective_refinement_session_id = @session_id ORDER BY sequence ASC, created_utc ASC;",
                cmd => cmd.Parameters.AddWithValue("@session_id", objectiveRefinementSessionId),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ObjectiveRefinementMessage>> EnumerateByObjectiveAsync(string objectiveId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(objectiveId)) throw new ArgumentNullException(nameof(objectiveId));
            return await EnumerateAsync(
                "SELECT * FROM objective_refinement_messages WHERE objective_id = @objective_id ORDER BY created_utc ASC, sequence ASC;",
                cmd => cmd.Parameters.AddWithValue("@objective_id", objectiveId),
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<ObjectiveRefinementMessage>> EnumeratePageBySessionAsync(string objectiveRefinementSessionId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(objectiveRefinementSessionId)) throw new ArgumentNullException(nameof(objectiveRefinementSessionId));
            if (query == null) throw new ArgumentNullException(nameof(query));
            string direction = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                return await MysqlBoundedPage.ReadAsync(conn,
                    "FROM objective_refinement_messages WHERE objective_refinement_session_id = @session_id",
                    "sequence " + direction + ", created_utc " + direction + ", id " + direction,
                    cmd => MysqlCommandHelper.Add(cmd, "@session_id", objectiveRefinementSessionId),
                    FromReader, query, token).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task<long> CountBySessionAsync(string objectiveRefinementSessionId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(objectiveRefinementSessionId)) throw new ArgumentNullException(nameof(objectiveRefinementSessionId));
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                return await MysqlCommandHelper.CountAsync(conn,
                    "SELECT COUNT(*) FROM objective_refinement_messages WHERE objective_refinement_session_id = @session_id;",
                    cmd => MysqlCommandHelper.Add(cmd, "@session_id", objectiveRefinementSessionId), token).ConfigureAwait(false);
            }
        }

        private async Task<List<ObjectiveRefinementMessage>> EnumerateAsync(string sql, Action<MySqlCommand> parameterize, CancellationToken token)
        {
            List<ObjectiveRefinementMessage> results = new List<ObjectiveRefinementMessage>();
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    parameterize(cmd);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(FromReader(reader));
                    }
                }
            }

            return results;
        }

        private static void Bind(MySqlCommand cmd, ObjectiveRefinementMessage message)
        {
            cmd.Parameters.AddWithValue("@id", message.Id);
            cmd.Parameters.AddWithValue("@objective_refinement_session_id", message.ObjectiveRefinementSessionId);
            cmd.Parameters.AddWithValue("@objective_id", message.ObjectiveId);
            cmd.Parameters.AddWithValue("@tenant_id", (object?)message.TenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user_id", (object?)message.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@role", message.Role);
            cmd.Parameters.AddWithValue("@sequence", message.Sequence);
            cmd.Parameters.AddWithValue("@content", message.Content);
            cmd.Parameters.AddWithValue("@is_selected", message.IsSelected ? 1 : 0);
            cmd.Parameters.AddWithValue("@created_utc", MysqlDatabaseDriver.ToIso8601(message.CreatedUtc));
            cmd.Parameters.AddWithValue("@last_update_utc", MysqlDatabaseDriver.ToIso8601(message.LastUpdateUtc));
        }

        private static ObjectiveRefinementMessage FromReader(MySqlDataReader reader)
        {
            return new ObjectiveRefinementMessage
            {
                Id = reader["id"].ToString()!,
                ObjectiveRefinementSessionId = reader["objective_refinement_session_id"].ToString()!,
                ObjectiveId = reader["objective_id"].ToString()!,
                TenantId = MysqlDatabaseDriver.NullableString(reader["tenant_id"]),
                UserId = MysqlDatabaseDriver.NullableString(reader["user_id"]),
                Role = reader["role"].ToString()!,
                Sequence = Convert.ToInt32(reader["sequence"]),
                Content = MysqlDatabaseDriver.NullableString(reader["content"]) ?? String.Empty,
                IsSelected = Convert.ToInt64(reader["is_selected"]) == 1,
                CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc),
                LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc)
            };
        }
    }
}
