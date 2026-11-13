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
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// PostgreSQL implementation of model alert persistence.
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
        /// <param name="driver">PostgreSQL database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public ModelAlertMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<ModelAlert> CreateAsync(ModelAlert alert, string alertKey, CancellationToken token = default)
        {
            if (alert == null) throw new ArgumentNullException(nameof(alert));
            if (String.IsNullOrEmpty(alertKey)) throw new ArgumentNullException(nameof(alertKey));
            alert.LastUpdateUtc = DateTime.UtcNow;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", alert.TenantId);
                    PostgresqlCommandHelper.Add(cmd, "@alert_key", alertKey);
                    PostgresqlCommandHelper.Add(cmd, "@profile_id", alert.ProfileId);
                    PostgresqlCommandHelper.Add(cmd, "@revision_group", alert.RevisionGroup);
                    PostgresqlCommandHelper.Add(cmd, "@persona", alert.Persona);
                    PostgresqlCommandHelper.Add(cmd, "@vessel_id", String.IsNullOrEmpty(alert.VesselId) ? null : alert.VesselId);
                    PostgresqlCommandHelper.Add(cmd, "@rule", alert.Rule.ToString());
                    PostgresqlCommandHelper.AddDate(cmd, "@created_utc", alert.CreatedUtc);
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
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Update, cmd => BindMutable(cmd, alert), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return alert;
        }

        /// <inheritdoc />
        public async Task<ModelAlert?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<ModelAlert> rows = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM model_alerts WHERE id = @id;",
                cmd => PostgresqlCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<ModelAlert?> ReadByKeyAsync(string alertKey, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(alertKey)) throw new ArgumentNullException(nameof(alertKey));
            List<ModelAlert> rows = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM model_alerts WHERE alert_key = @alert_key;",
                cmd => PostgresqlCommandHelper.Add(cmd, "@alert_key", alertKey), FromReader, token).ConfigureAwait(false);
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

            Action<NpgsqlCommand> bind = cmd =>
            {
                if (query.TenantId != null) PostgresqlCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (query.Status.HasValue) PostgresqlCommandHelper.Add(cmd, "@status", query.Status.Value.ToString());
                if (query.ProfileId != null) PostgresqlCommandHelper.Add(cmd, "@profile_id", query.ProfileId);
                if (query.Persona != null) PostgresqlCommandHelper.Add(cmd, "@persona", query.Persona.ToLowerInvariant());
                if (query.NotIgnoredAtUtc.HasValue) PostgresqlCommandHelper.AddDate(cmd, "@now", query.NotIgnoredAtUtc.Value);
            };

            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            using (NpgsqlConnection conn = new NpgsqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await PostgresqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM model_alerts" + where + ";", bind, token).ConfigureAwait(false);
                List<ModelAlert> rows = await PostgresqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM model_alerts" + where + " ORDER BY last_raised_utc DESC, id DESC" + PostgresqlCommandHelper.Page(offset, pageSize) + ";",
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
            return await PostgresqlCommandHelper.QueryAsync(_ConnectionString, sql, cmd =>
            {
                PostgresqlCommandHelper.Add(cmd, "@status", ModelAlertStatusEnum.Open.ToString());
                if (tenantId != null) PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
            }, FromReader, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void BindMutable(NpgsqlCommand cmd, ModelAlert alert)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", alert.Id);
            PostgresqlCommandHelper.Add(cmd, "@model", alert.Model);
            PostgresqlCommandHelper.Add(cmd, "@vessel_name", alert.VesselName);
            PostgresqlCommandHelper.Add(cmd, "@status", alert.Status.ToString());
            PostgresqlCommandHelper.Add(cmd, "@title", alert.Title);
            PostgresqlCommandHelper.Add(cmd, "@detail", alert.Detail);
            PostgresqlCommandHelper.Add(cmd, "@metric_count", alert.Count);
            PostgresqlCommandHelper.Add(cmd, "@metric_of", alert.Of);
            PostgresqlCommandHelper.Add(cmd, "@metric_value", alert.Value);
            PostgresqlCommandHelper.Add(cmd, "@threshold_value", alert.Threshold);
            PostgresqlCommandHelper.AddDate(cmd, "@last_raised_utc", alert.LastRaisedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@last_evaluated_utc", alert.LastEvaluatedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@resolved_utc", alert.ResolvedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@ignored_until_utc", alert.IgnoredUntilUtc);
            PostgresqlCommandHelper.Add(cmd, "@actions_json", ModelAlertActionsJson.Serialize(alert.Actions));
            PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", alert.LastUpdateUtc);
        }

        private static ModelAlert FromReader(NpgsqlDataReader reader)
        {
            ModelAlert alert = new ModelAlert();
            alert.Id = reader["id"].ToString()!;
            alert.TenantId = PostgresqlCommandHelper.ReadString(reader["tenant_id"]);
            alert.ProfileId = reader["profile_id"].ToString()!;
            alert.Model = PostgresqlCommandHelper.ReadString(reader["model"]);
            alert.RevisionGroup = PostgresqlCommandHelper.ReadString(reader["revision_group"]) ?? String.Empty;
            alert.Persona = PostgresqlCommandHelper.ReadString(reader["persona"]) ?? String.Empty;
            alert.VesselId = PostgresqlCommandHelper.ReadString(reader["vessel_id"]);
            alert.VesselName = PostgresqlCommandHelper.ReadString(reader["vessel_name"]);
            alert.Rule = PostgresqlCommandHelper.ReadEnum(reader["rule"], ModelAlertRuleEnum.FirstPassSuccess);
            alert.Status = PostgresqlCommandHelper.ReadEnum(reader["status"], ModelAlertStatusEnum.Open);
            alert.Title = PostgresqlCommandHelper.ReadString(reader["title"]) ?? String.Empty;
            alert.Detail = PostgresqlCommandHelper.ReadString(reader["detail"]) ?? String.Empty;
            alert.Count = PostgresqlCommandHelper.ReadNullableInt(reader["metric_count"]);
            alert.Of = PostgresqlCommandHelper.ReadNullableInt(reader["metric_of"]);
            alert.Value = PostgresqlCommandHelper.ReadNullableDouble(reader["metric_value"]);
            alert.Threshold = PostgresqlCommandHelper.ReadNullableDouble(reader["threshold_value"]);
            alert.CreatedUtc = PostgresqlCommandHelper.ReadDate(reader["created_utc"]);
            alert.LastRaisedUtc = PostgresqlCommandHelper.ReadDate(reader["last_raised_utc"]);
            alert.LastEvaluatedUtc = PostgresqlCommandHelper.ReadDate(reader["last_evaluated_utc"]);
            alert.ResolvedUtc = PostgresqlCommandHelper.ReadNullableDate(reader["resolved_utc"]);
            alert.IgnoredUntilUtc = PostgresqlCommandHelper.ReadNullableDate(reader["ignored_until_utc"]);
            alert.Actions = ModelAlertActionsJson.Deserialize(PostgresqlCommandHelper.ReadString(reader["actions_json"]));
            alert.LastUpdateUtc = PostgresqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return alert;
        }

        #endregion
    }
}
