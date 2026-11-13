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
    /// MySQL implementation of runbook reads: runbooks are playbook rows, so they are filtered, paged, and counted in
    /// the playbooks table.
    /// </summary>
    public class RunbookMethods : IRunbookMethods
    {
        #region Private-Members

        private const string _MetadataMarker = "<!-- armada:runbook";
        private readonly string _ConnectionString;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">Connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public RunbookMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<EnumerationResult<Playbook>> EnumerateAsync(RunbookQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            List<string> conditions = new List<string>();
            if (!String.IsNullOrWhiteSpace(query.TenantId)) conditions.Add("tenant_id = @tenant_id");
            if (!String.IsNullOrWhiteSpace(query.VisibleToUserId)) conditions.Add("(scope = @tenant_wide OR user_id = @visible_user_id)");
            if (query.Active.HasValue) conditions.Add("active = @active");
            if (!String.IsNullOrWhiteSpace(query.WorkflowProfileId)) conditions.Add("content LIKE @metadata_prefix ESCAPE '!' AND content LIKE @workflow_profile ESCAPE '!'");
            if (!String.IsNullOrWhiteSpace(query.EnvironmentId)) conditions.Add("content LIKE @metadata_prefix ESCAPE '!' AND content LIKE @environment ESCAPE '!'");
            if (query.DefaultCheckType.HasValue) conditions.Add("content LIKE @metadata_prefix ESCAPE '!' AND content LIKE @check_type ESCAPE '!'");
            if (!String.IsNullOrWhiteSpace(query.Search)) conditions.Add("(LOWER(file_name) LIKE @search ESCAPE '!' OR LOWER(COALESCE(description, '')) LIKE @search ESCAPE '!' OR LOWER(content) LIKE @search ESCAPE '!')");
            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;
            Action<MySqlCommand> bind = cmd =>
            {
                if (!String.IsNullOrWhiteSpace(query.TenantId)) MysqlCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (!String.IsNullOrWhiteSpace(query.VisibleToUserId))
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_wide", ScopeEnum.TenantWide.ToString());
                    MysqlCommandHelper.Add(cmd, "@visible_user_id", query.VisibleToUserId);
                }
                if (query.Active.HasValue) MysqlCommandHelper.Add(cmd, "@active", query.Active.Value);
                if (!String.IsNullOrWhiteSpace(query.WorkflowProfileId) || !String.IsNullOrWhiteSpace(query.EnvironmentId) || query.DefaultCheckType.HasValue)
                    MysqlCommandHelper.Add(cmd, "@metadata_prefix", EscapeLike(_MetadataMarker) + "%");
                if (!String.IsNullOrWhiteSpace(query.WorkflowProfileId)) MysqlCommandHelper.Add(cmd, "@workflow_profile", "%" + EscapeLike("\"WorkflowProfileId\":\"" + query.WorkflowProfileId.Trim() + "\"") + "%");
                if (!String.IsNullOrWhiteSpace(query.EnvironmentId)) MysqlCommandHelper.Add(cmd, "@environment", "%" + EscapeLike("\"EnvironmentId\":\"" + query.EnvironmentId.Trim() + "\"") + "%");
                if (query.DefaultCheckType.HasValue) MysqlCommandHelper.Add(cmd, "@check_type", "%" + EscapeLike("\"DefaultCheckType\":\"" + query.DefaultCheckType.Value.ToString() + "\"") + "%");
                if (!String.IsNullOrWhiteSpace(query.Search)) MysqlCommandHelper.Add(cmd, "@search", "%" + EscapeLike(query.Search.Trim().ToLowerInvariant()) + "%");
            };

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await MysqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM playbooks" + where + ";", bind, token).ConfigureAwait(false);
                List<Playbook> rows = await MysqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM playbooks" + where + " ORDER BY file_name ASC, id ASC" + MysqlCommandHelper.Page(query.Offset, query.PageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);

                EnumerationResult<Playbook> result = new EnumerationResult<Playbook>();
                result.PageNumber = query.PageNumber;
                result.PageSize = query.PageSize;
                result.TotalRecords = total;
                result.TotalPages = (int)Math.Ceiling((double)total / query.PageSize);
                result.Objects = rows;
                return result;
            }
        }

        #endregion

        #region Private-Methods

        private static string EscapeLike(string value)
        {
            return value.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_").Replace("[", "![");
        }

        private static Playbook FromReader(MySqlDataReader reader)
        {
            return new Playbook
            {
                Id = reader["id"].ToString() ?? String.Empty,
                TenantId = MysqlCommandHelper.ReadString(reader["tenant_id"]),
                UserId = MysqlCommandHelper.ReadString(reader["user_id"]),
                Scope = Enum.TryParse(reader["scope"]?.ToString(), true, out ScopeEnum scope) ? scope : ScopeEnum.TenantWide,
                FileName = reader["file_name"].ToString() ?? String.Empty,
                Description = MysqlCommandHelper.ReadString(reader["description"]),
                Content = reader["content"].ToString() ?? String.Empty,
                Active = MysqlCommandHelper.ReadBool(reader["active"], true),
                CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]),
                LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"])
            };
        }

        #endregion
    }
}
