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
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of model alert persistence.
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
        private readonly SemaphoreSlim? _WriteLock;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when connectionString is null.</exception>
        public ModelAlertMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _WriteLock = null;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<ModelAlert> CreateAsync(ModelAlert alert, string alertKey, CancellationToken token = default)
        {
            if (alert == null) throw new ArgumentNullException(nameof(alert));
            if (String.IsNullOrEmpty(alertKey)) throw new ArgumentNullException(nameof(alertKey));
            alert.LastUpdateUtc = DateTime.UtcNow;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", alert.TenantId);
                    MysqlCommandHelper.Add(cmd, "@alert_key", alertKey);
                    MysqlCommandHelper.Add(cmd, "@profile_id", alert.ProfileId);
                    MysqlCommandHelper.Add(cmd, "@revision_group", alert.RevisionGroup);
                    MysqlCommandHelper.Add(cmd, "@persona", alert.Persona);
                    MysqlCommandHelper.Add(cmd, "@vessel_id", String.IsNullOrEmpty(alert.VesselId) ? null : alert.VesselId);
                    MysqlCommandHelper.Add(cmd, "@rule", alert.Rule.ToString());
                    MysqlCommandHelper.AddDate(cmd, "@created_utc", alert.CreatedUtc);
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
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Update, cmd => BindMutable(cmd, alert), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return alert;
        }

        /// <inheritdoc />
        public async Task<ModelAlert?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<ModelAlert> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM model_alerts WHERE id = @id;",
                cmd => MysqlCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<ModelAlert?> ReadByKeyAsync(string alertKey, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(alertKey)) throw new ArgumentNullException(nameof(alertKey));
            List<ModelAlert> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM model_alerts WHERE alert_key = @alert_key;",
                cmd => MysqlCommandHelper.Add(cmd, "@alert_key", alertKey), FromReader, token).ConfigureAwait(false);
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

            Action<MySqlCommand> bind = cmd =>
            {
                if (query.TenantId != null) MysqlCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (query.Status.HasValue) MysqlCommandHelper.Add(cmd, "@status", query.Status.Value.ToString());
                if (query.ProfileId != null) MysqlCommandHelper.Add(cmd, "@profile_id", query.ProfileId);
                if (query.Persona != null) MysqlCommandHelper.Add(cmd, "@persona", query.Persona.ToLowerInvariant());
                if (query.NotIgnoredAtUtc.HasValue) MysqlCommandHelper.AddDate(cmd, "@now", query.NotIgnoredAtUtc.Value);
            };

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await MysqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM model_alerts" + where + ";", bind, token).ConfigureAwait(false);
                List<ModelAlert> rows = await MysqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM model_alerts" + where + " ORDER BY last_raised_utc DESC, id DESC" + MysqlCommandHelper.Page(offset, pageSize) + ";",
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
            return await MysqlCommandHelper.QueryAsync(_ConnectionString, sql, cmd =>
            {
                MysqlCommandHelper.Add(cmd, "@status", ModelAlertStatusEnum.Open.ToString());
                if (tenantId != null) MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
            }, FromReader, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void BindMutable(MySqlCommand cmd, ModelAlert alert)
        {
            MysqlCommandHelper.Add(cmd, "@id", alert.Id);
            MysqlCommandHelper.Add(cmd, "@model", alert.Model);
            MysqlCommandHelper.Add(cmd, "@vessel_name", alert.VesselName);
            MysqlCommandHelper.Add(cmd, "@status", alert.Status.ToString());
            MysqlCommandHelper.Add(cmd, "@title", alert.Title);
            MysqlCommandHelper.Add(cmd, "@detail", alert.Detail);
            MysqlCommandHelper.Add(cmd, "@metric_count", alert.Count);
            MysqlCommandHelper.Add(cmd, "@metric_of", alert.Of);
            MysqlCommandHelper.Add(cmd, "@metric_value", alert.Value);
            MysqlCommandHelper.Add(cmd, "@threshold_value", alert.Threshold);
            MysqlCommandHelper.AddDate(cmd, "@last_raised_utc", alert.LastRaisedUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_evaluated_utc", alert.LastEvaluatedUtc);
            MysqlCommandHelper.AddDate(cmd, "@resolved_utc", alert.ResolvedUtc);
            MysqlCommandHelper.AddDate(cmd, "@ignored_until_utc", alert.IgnoredUntilUtc);
            MysqlCommandHelper.Add(cmd, "@actions_json", ModelAlertActionsJson.Serialize(alert.Actions));
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", alert.LastUpdateUtc);
        }

        private static ModelAlert FromReader(MySqlDataReader reader)
        {
            ModelAlert alert = new ModelAlert();
            alert.Id = reader["id"].ToString()!;
            alert.TenantId = MysqlCommandHelper.ReadString(reader["tenant_id"]);
            alert.ProfileId = reader["profile_id"].ToString()!;
            alert.Model = MysqlCommandHelper.ReadString(reader["model"]);
            alert.RevisionGroup = MysqlCommandHelper.ReadString(reader["revision_group"]) ?? String.Empty;
            alert.Persona = MysqlCommandHelper.ReadString(reader["persona"]) ?? String.Empty;
            alert.VesselId = MysqlCommandHelper.ReadString(reader["vessel_id"]);
            alert.VesselName = MysqlCommandHelper.ReadString(reader["vessel_name"]);
            alert.Rule = MysqlCommandHelper.ReadEnum(reader["rule"], ModelAlertRuleEnum.FirstPassSuccess);
            alert.Status = MysqlCommandHelper.ReadEnum(reader["status"], ModelAlertStatusEnum.Open);
            alert.Title = MysqlCommandHelper.ReadString(reader["title"]) ?? String.Empty;
            alert.Detail = MysqlCommandHelper.ReadString(reader["detail"]) ?? String.Empty;
            alert.Count = MysqlCommandHelper.ReadNullableInt(reader["metric_count"]);
            alert.Of = MysqlCommandHelper.ReadNullableInt(reader["metric_of"]);
            alert.Value = MysqlCommandHelper.ReadNullableDouble(reader["metric_value"]);
            alert.Threshold = MysqlCommandHelper.ReadNullableDouble(reader["threshold_value"]);
            alert.CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]);
            alert.LastRaisedUtc = MysqlCommandHelper.ReadDate(reader["last_raised_utc"]);
            alert.LastEvaluatedUtc = MysqlCommandHelper.ReadDate(reader["last_evaluated_utc"]);
            alert.ResolvedUtc = MysqlCommandHelper.ReadNullableDate(reader["resolved_utc"]);
            alert.IgnoredUntilUtc = MysqlCommandHelper.ReadNullableDate(reader["ignored_until_utc"]);
            alert.Actions = ModelAlertActionsJson.Deserialize(MysqlCommandHelper.ReadString(reader["actions_json"]));
            alert.LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return alert;
        }

        #endregion
    }
}
