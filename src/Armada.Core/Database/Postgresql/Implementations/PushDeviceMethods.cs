namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
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
    /// PostgreSQL implementation of push notification device persistence.
    /// </summary>
    public class PushDeviceMethods : IPushDeviceMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO push_devices
            (id, tenant_id, user_id, platform, expo_push_token, device_name, app_version, locale, categories, active, created_utc, last_seen_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @user_id, @platform, @expo_push_token, @device_name, @app_version, @locale, @categories, @active, @created_utc, @last_seen_utc, @last_update_utc);";

        private static readonly string _Update = @"UPDATE push_devices SET
            tenant_id = @tenant_id, user_id = @user_id, platform = @platform, device_name = @device_name, app_version = @app_version,
            locale = @locale, categories = @categories, active = @active, last_seen_utc = @last_seen_utc, last_update_utc = @last_update_utc
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
        public PushDeviceMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<PushDevice> CreateAsync(PushDevice device, CancellationToken token = default)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            if (String.IsNullOrEmpty(device.ExpoPushToken)) throw new ArgumentException("ExpoPushToken is required.", nameof(device));
            device.LastUpdateUtc = DateTime.UtcNow;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, device), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return device;
        }

        /// <inheritdoc />
        public async Task<PushDevice?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<PushDevice> rows = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM push_devices WHERE id = @id;",
                cmd => PostgresqlCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<PushDevice?> ReadByTokenAsync(string expoPushToken, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(expoPushToken)) throw new ArgumentNullException(nameof(expoPushToken));
            List<PushDevice> rows = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM push_devices WHERE expo_push_token = @expo_push_token;",
                cmd => PostgresqlCommandHelper.Add(cmd, "@expo_push_token", expoPushToken), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<PushDevice> UpdateAsync(PushDevice device, CancellationToken token = default)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            device.LastUpdateUtc = DateTime.UtcNow;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Update, cmd => Bind(cmd, device), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return device;
        }

        /// <inheritdoc />
        public async Task<bool> SetActiveAsync(string id, bool active, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            int updated = 0;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                updated = await PostgresqlCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE push_devices SET active = @active, last_update_utc = @now WHERE id = @id;",
                    cmd =>
                    {
                        PostgresqlCommandHelper.Add(cmd, "@active", active);
                        PostgresqlCommandHelper.AddDate(cmd, "@now", DateTime.UtcNow);
                        PostgresqlCommandHelper.Add(cmd, "@id", id);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return updated > 0;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            int deleted = 0;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                deleted = await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM push_devices WHERE id = @id;",
                    cmd => PostgresqlCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted > 0;
        }

        /// <inheritdoc />
        public async Task<int> DeleteByUserAsync(string tenantId, string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            int deleted = 0;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                deleted = await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM push_devices WHERE tenant_id = @tenant_id AND user_id = @user_id;",
                    cmd =>
                    {
                        PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                        PostgresqlCommandHelper.Add(cmd, "@user_id", userId);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        /// <inheritdoc />
        public async Task<int> DeleteByTenantAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            int deleted = 0;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                deleted = await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM push_devices WHERE tenant_id = @tenant_id;",
                    cmd => PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        /// <inheritdoc />
        public async Task<List<PushDevice>> EnumerateAsync(PushDeviceQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            StringBuilder sql = new StringBuilder("SELECT * FROM push_devices WHERE 1 = 1");
            if (query.TenantId != null) sql.Append(" AND tenant_id = @tenant_id");
            if (query.UserId != null) sql.Append(" AND user_id = @user_id");
            if (query.ActiveOnly) sql.Append(" AND active = @active");
            sql.Append(" ORDER BY created_utc ASC, id ASC;");

            return await PostgresqlCommandHelper.QueryAsync(_ConnectionString, sql.ToString(), cmd =>
            {
                if (query.TenantId != null) PostgresqlCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (query.UserId != null) PostgresqlCommandHelper.Add(cmd, "@user_id", query.UserId);
                if (query.ActiveOnly) PostgresqlCommandHelper.Add(cmd, "@active", true);
            }, FromReader, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Bind(NpgsqlCommand cmd, PushDevice device)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", device.Id);
            PostgresqlCommandHelper.Add(cmd, "@tenant_id", device.TenantId);
            PostgresqlCommandHelper.Add(cmd, "@user_id", device.UserId);
            PostgresqlCommandHelper.Add(cmd, "@platform", device.Platform.ToString());
            PostgresqlCommandHelper.Add(cmd, "@expo_push_token", device.ExpoPushToken);
            PostgresqlCommandHelper.Add(cmd, "@device_name", device.DeviceName);
            PostgresqlCommandHelper.Add(cmd, "@app_version", device.AppVersion);
            PostgresqlCommandHelper.Add(cmd, "@locale", device.Locale);
            PostgresqlCommandHelper.Add(cmd, "@categories", PushDeviceCategoriesColumn.Format(device.Categories));
            PostgresqlCommandHelper.Add(cmd, "@active", device.Active);
            PostgresqlCommandHelper.AddDate(cmd, "@created_utc", device.CreatedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@last_seen_utc", device.LastSeenUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", device.LastUpdateUtc);
        }

        private static PushDevice FromReader(NpgsqlDataReader reader)
        {
            PushDevice device = new PushDevice();
            device.Id = reader["id"].ToString()!;
            device.TenantId = PostgresqlCommandHelper.ReadString(reader["tenant_id"]);
            device.UserId = PostgresqlCommandHelper.ReadString(reader["user_id"]);
            device.Platform = PostgresqlCommandHelper.ReadEnum(reader["platform"], PushPlatformEnum.Ios);
            device.ExpoPushToken = reader["expo_push_token"]?.ToString() ?? String.Empty;
            device.DeviceName = PostgresqlCommandHelper.ReadString(reader["device_name"]);
            device.AppVersion = PostgresqlCommandHelper.ReadString(reader["app_version"]);
            device.Locale = PostgresqlCommandHelper.ReadString(reader["locale"]);
            device.Categories = PushDeviceCategoriesColumn.Parse(PostgresqlCommandHelper.ReadString(reader["categories"]));
            device.Active = PostgresqlCommandHelper.ReadBool(reader["active"], true);
            device.CreatedUtc = PostgresqlCommandHelper.ReadDate(reader["created_utc"]);
            device.LastSeenUtc = PostgresqlCommandHelper.ReadDate(reader["last_seen_utc"]);
            device.LastUpdateUtc = PostgresqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return device;
        }

        #endregion
    }
}
