namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQLite implementation of runbook reads: runbooks are playbook rows, so they are filtered, paged, and counted in
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
        /// <param name="driver">SQLite database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public RunbookMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _ConnectionString = driver.ConnectionString;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<EnumerationResult<Playbook>> EnumerateAsync(RunbookQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            List<string> conditions = new List<string>();
            conditions.Add("content LIKE @metadata_prefix ESCAPE '!'");
            if (!String.IsNullOrWhiteSpace(query.TenantId)) conditions.Add("tenant_id = @tenant_id");
            if (!String.IsNullOrWhiteSpace(query.VisibleToUserId)) conditions.Add("(scope = @tenant_wide OR user_id = @visible_user_id)");
            if (query.Active.HasValue) conditions.Add("active = @active");
            if (!String.IsNullOrWhiteSpace(query.WorkflowProfileId)) conditions.Add("content LIKE @metadata_prefix ESCAPE '!' AND content LIKE @workflow_profile ESCAPE '!'");
            if (!String.IsNullOrWhiteSpace(query.EnvironmentId)) conditions.Add("content LIKE @metadata_prefix ESCAPE '!' AND content LIKE @environment ESCAPE '!'");
            if (query.DefaultCheckType.HasValue) conditions.Add("content LIKE @metadata_prefix ESCAPE '!' AND content LIKE @check_type ESCAPE '!'");
            if (!String.IsNullOrWhiteSpace(query.Search)) conditions.Add("(LOWER(file_name) LIKE @search ESCAPE '!' OR LOWER(COALESCE(description, '')) LIKE @search ESCAPE '!' OR LOWER(content) LIKE @search ESCAPE '!')");
            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;
            Action<SqliteCommand> bind = cmd =>
            {
                if (!String.IsNullOrWhiteSpace(query.TenantId)) SqliteCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (!String.IsNullOrWhiteSpace(query.VisibleToUserId))
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_wide", ScopeEnum.TenantWide.ToString());
                    SqliteCommandHelper.Add(cmd, "@visible_user_id", query.VisibleToUserId);
                }
                if (query.Active.HasValue) SqliteCommandHelper.Add(cmd, "@active", query.Active.Value);
                SqliteCommandHelper.Add(cmd, "@metadata_prefix", EscapeLike(_MetadataMarker) + "%");
                if (!String.IsNullOrWhiteSpace(query.WorkflowProfileId)) SqliteCommandHelper.Add(cmd, "@workflow_profile", "%" + EscapeLike("\"WorkflowProfileId\":\"" + query.WorkflowProfileId.Trim() + "\"") + "%");
                if (!String.IsNullOrWhiteSpace(query.EnvironmentId)) SqliteCommandHelper.Add(cmd, "@environment", "%" + EscapeLike("\"EnvironmentId\":\"" + query.EnvironmentId.Trim() + "\"") + "%");
                if (query.DefaultCheckType.HasValue) SqliteCommandHelper.Add(cmd, "@check_type", "%" + EscapeLike("\"DefaultCheckType\":\"" + query.DefaultCheckType.Value.ToString() + "\"") + "%");
                if (!String.IsNullOrWhiteSpace(query.Search)) SqliteCommandHelper.Add(cmd, "@search", "%" + EscapeLike(query.Search.Trim().ToLowerInvariant()) + "%");
            };

            using (SqliteConnection conn = new SqliteProviderConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqliteCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM playbooks" + where + ";", bind, token).ConfigureAwait(false);
                List<Playbook> rows = await SqliteCommandHelper.QueryAsync(conn,
                    "SELECT * FROM playbooks" + where + " ORDER BY file_name ASC, id ASC" + SqliteCommandHelper.Page(query.Offset, query.PageSize) + ";",
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

        private static Playbook FromReader(SqliteDataReader reader)
        {
            return new Playbook
            {
                Id = reader["id"].ToString() ?? String.Empty,
                TenantId = SqliteCommandHelper.ReadString(reader["tenant_id"]),
                UserId = SqliteCommandHelper.ReadString(reader["user_id"]),
                Scope = Enum.TryParse(reader["scope"]?.ToString(), true, out ScopeEnum scope) ? scope : ScopeEnum.TenantWide,
                FileName = reader["file_name"].ToString() ?? String.Empty,
                Description = SqliteCommandHelper.ReadString(reader["description"]),
                Content = reader["content"].ToString() ?? String.Empty,
                Active = SqliteCommandHelper.ReadBool(reader["active"], true),
                CreatedUtc = SqliteCommandHelper.ReadDate(reader["created_utc"]),
                LastUpdateUtc = SqliteCommandHelper.ReadDate(reader["last_update_utc"])
            };
        }

        #endregion
    }
}
