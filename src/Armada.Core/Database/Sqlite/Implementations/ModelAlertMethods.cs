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
    /// SQLite implementation of model alert persistence.
    /// </summary>
    public class ModelAlertMethods : IModelAlertMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO model_alerts
            (id, tenant_id, alert_key, profile_id, model, revision_group, persona, vessel_id, vessel_name, rule, status, title, detail,
             metric_count, metric_of, metric_value, threshold_value, created_utc, last_raised_utc, last_evaluated_utc, resolved_utc,
             ignored_until_utc, actions_json, last_update_utc)
            VALUES
            (@id, @tenant_id, @alert_key, @profile_id, @model, @revision_group, @persona, @vessel_id, @vessel_name, @rule, @status, @title, @detail,
             @metric_count, @metric_of, @metric_value, @threshold_value, @created_utc, @last_raised_utc, @last_evaluated_utc, @resolved_utc,
             @ignored_until_utc, @actions_json, @last_update_utc);";

        private static readonly string _Update = @"UPDATE model_alerts SET
            model = @model, vessel_name = @vessel_name, status = @status, title = @title, detail = @detail, metric_count = @metric_count,
            metric_of = @metric_of, metric_value = @metric_value, threshold_value = @threshold_value, last_raised_utc = @last_raised_utc,
            last_evaluated_utc = @last_evaluated_utc, resolved_utc = @resolved_utc, ignored_until_utc = @ignored_until_utc,
            actions_json = @actions_json, last_update_utc = @last_update_utc
            WHERE id = @id;";

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
        public ModelAlertMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<ModelAlert> CreateAsync(ModelAlert alert, string alertKey, CancellationToken token = default)
        {
            if (alert == null) throw new ArgumentNullException(nameof(alert));
            if (String.IsNullOrEmpty(alertKey)) throw new ArgumentNullException(nameof(alertKey));
            alert.LastUpdateUtc = DateTime.UtcNow;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", alert.TenantId);
                    SqliteCommandHelper.Add(cmd, "@alert_key", alertKey);
                    SqliteCommandHelper.Add(cmd, "@profile_id", alert.ProfileId);
                    SqliteCommandHelper.Add(cmd, "@revision_group", alert.RevisionGroup);
                    SqliteCommandHelper.Add(cmd, "@persona", alert.Persona);
                    SqliteCommandHelper.Add(cmd, "@vessel_id", String.IsNullOrEmpty(alert.VesselId) ? null : alert.VesselId);
                    SqliteCommandHelper.Add(cmd, "@rule", alert.Rule.ToString());
                    SqliteCommandHelper.AddDate(cmd, "@created_utc", alert.CreatedUtc);
                    BindMutable(cmd, alert);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return alert;
        }

        /// <inheritdoc />
        public async Task<ModelAlert> UpdateAsync(ModelAlert alert, CancellationToken token = default)
        {
            if (alert == null) throw new ArgumentNullException(nameof(alert));
            alert.LastUpdateUtc = DateTime.UtcNow;
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteGate, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, _Update, cmd => BindMutable(cmd, alert), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return alert;
        }

        /// <inheritdoc />
        public async Task<ModelAlert?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<ModelAlert> rows = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM model_alerts WHERE id = @id;",
                cmd => SqliteCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<ModelAlert?> ReadByKeyAsync(string alertKey, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(alertKey)) throw new ArgumentNullException(nameof(alertKey));
            List<ModelAlert> rows = await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM model_alerts WHERE alert_key = @alert_key;",
                cmd => SqliteCommandHelper.Add(cmd, "@alert_key", alertKey), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<ModelAlert>> EnumerateAsync(ModelAlertQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            int pageSize = query.PageSize < 1 ? 1 : (query.PageSize > 1000 ? 1000 : query.PageSize);
            int pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
            int offset = (pageNumber - 1) * pageSize;

            List<string> conditions = new List<string>();
            if (query.TenantId != null) conditions.Add("tenant_id = @tenant_id");
            if (query.Status.HasValue) conditions.Add("status = @status");
            if (query.ProfileId != null) conditions.Add("profile_id = @profile_id");
            if (query.Persona != null) conditions.Add("LOWER(persona) = @persona");
            if (query.NotIgnoredAtUtc.HasValue) conditions.Add("(ignored_until_utc IS NULL OR ignored_until_utc <= @now)");

            Action<SqliteCommand> bind = cmd =>
            {
                if (query.TenantId != null) SqliteCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (query.Status.HasValue) SqliteCommandHelper.Add(cmd, "@status", query.Status.Value.ToString());
                if (query.ProfileId != null) SqliteCommandHelper.Add(cmd, "@profile_id", query.ProfileId);
                if (query.Persona != null) SqliteCommandHelper.Add(cmd, "@persona", query.Persona.ToLowerInvariant());
                if (query.NotIgnoredAtUtc.HasValue) SqliteCommandHelper.AddDate(cmd, "@now", query.NotIgnoredAtUtc.Value);
            };

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (SqliteConnection conn = new SqliteProviderConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqliteCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM model_alerts" + where + ";", bind, token).ConfigureAwait(false);
                List<ModelAlert> rows = await SqliteCommandHelper.QueryAsync(conn,
                    "SELECT * FROM model_alerts" + where + " ORDER BY last_raised_utc DESC, id DESC" + SqliteCommandHelper.Page(offset, pageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);

                EnumerationResult<ModelAlert> result = new EnumerationResult<ModelAlert>();
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalRecords = total;
                result.TotalPages = (int)Math.Ceiling((double)total / pageSize);
                result.Objects = rows;
                return result;
            }
        }

        /// <inheritdoc />
        public async Task<List<ModelAlert>> EnumerateOpenAsync(string? tenantId, CancellationToken token = default)
        {
            string sql = "SELECT * FROM model_alerts WHERE status = @status" + (tenantId != null ? " AND tenant_id = @tenant_id" : "") + " ORDER BY id;";
            return await SqliteCommandHelper.QueryAsync(_ConnectionString, sql, cmd =>
            {
                SqliteCommandHelper.Add(cmd, "@status", ModelAlertStatusEnum.Open.ToString());
                if (tenantId != null) SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
            }, FromReader, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void BindMutable(SqliteCommand cmd, ModelAlert alert)
        {
            SqliteCommandHelper.Add(cmd, "@id", alert.Id);
            SqliteCommandHelper.Add(cmd, "@model", alert.Model);
            SqliteCommandHelper.Add(cmd, "@vessel_name", alert.VesselName);
            SqliteCommandHelper.Add(cmd, "@status", alert.Status.ToString());
            SqliteCommandHelper.Add(cmd, "@title", alert.Title);
            SqliteCommandHelper.Add(cmd, "@detail", alert.Detail);
            SqliteCommandHelper.Add(cmd, "@metric_count", alert.Count);
            SqliteCommandHelper.Add(cmd, "@metric_of", alert.Of);
            SqliteCommandHelper.Add(cmd, "@metric_value", alert.Value);
            SqliteCommandHelper.Add(cmd, "@threshold_value", alert.Threshold);
            SqliteCommandHelper.AddDate(cmd, "@last_raised_utc", alert.LastRaisedUtc);
            SqliteCommandHelper.AddDate(cmd, "@last_evaluated_utc", alert.LastEvaluatedUtc);
            SqliteCommandHelper.AddDate(cmd, "@resolved_utc", alert.ResolvedUtc);
            SqliteCommandHelper.AddDate(cmd, "@ignored_until_utc", alert.IgnoredUntilUtc);
            SqliteCommandHelper.Add(cmd, "@actions_json", ModelAlertActionsJson.Serialize(alert.Actions));
            SqliteCommandHelper.AddDate(cmd, "@last_update_utc", alert.LastUpdateUtc);
        }

        private static ModelAlert FromReader(SqliteDataReader reader)
        {
            ModelAlert alert = new ModelAlert();
            alert.Id = reader["id"].ToString()!;
            alert.TenantId = SqliteCommandHelper.ReadString(reader["tenant_id"]);
            alert.ProfileId = reader["profile_id"].ToString()!;
            alert.Model = SqliteCommandHelper.ReadString(reader["model"]);
            alert.RevisionGroup = SqliteCommandHelper.ReadString(reader["revision_group"]) ?? String.Empty;
            alert.Persona = SqliteCommandHelper.ReadString(reader["persona"]) ?? String.Empty;
            alert.VesselId = SqliteCommandHelper.ReadString(reader["vessel_id"]);
            alert.VesselName = SqliteCommandHelper.ReadString(reader["vessel_name"]);
            alert.Rule = SqliteCommandHelper.ReadEnum(reader["rule"], ModelAlertRuleEnum.FirstPassSuccess);
            alert.Status = SqliteCommandHelper.ReadEnum(reader["status"], ModelAlertStatusEnum.Open);
            alert.Title = SqliteCommandHelper.ReadString(reader["title"]) ?? String.Empty;
            alert.Detail = SqliteCommandHelper.ReadString(reader["detail"]) ?? String.Empty;
            alert.Count = SqliteCommandHelper.ReadNullableInt(reader["metric_count"]);
            alert.Of = SqliteCommandHelper.ReadNullableInt(reader["metric_of"]);
            alert.Value = SqliteCommandHelper.ReadNullableDouble(reader["metric_value"]);
            alert.Threshold = SqliteCommandHelper.ReadNullableDouble(reader["threshold_value"]);
            alert.CreatedUtc = SqliteCommandHelper.ReadDate(reader["created_utc"]);
            alert.LastRaisedUtc = SqliteCommandHelper.ReadDate(reader["last_raised_utc"]);
            alert.LastEvaluatedUtc = SqliteCommandHelper.ReadDate(reader["last_evaluated_utc"]);
            alert.ResolvedUtc = SqliteCommandHelper.ReadNullableDate(reader["resolved_utc"]);
            alert.IgnoredUntilUtc = SqliteCommandHelper.ReadNullableDate(reader["ignored_until_utc"]);
            alert.Actions = ModelAlertActionsJson.Deserialize(SqliteCommandHelper.ReadString(reader["actions_json"]));
            alert.LastUpdateUtc = SqliteCommandHelper.ReadDate(reader["last_update_utc"]);
            return alert;
        }

        #endregion
    }
}
