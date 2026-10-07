namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of push notification device persistence.
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
        /// <param name="connectionString">MySQL connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when connectionString is null.</exception>
        public PushDeviceMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
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
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, device), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return device;
        }

        /// <inheritdoc />
        public async Task<PushDevice?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<PushDevice> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM push_devices WHERE id = @id;",
                cmd => MysqlCommandHelper.Add(cmd, "@id", id), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<PushDevice?> ReadByTokenAsync(string expoPushToken, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(expoPushToken)) throw new ArgumentNullException(nameof(expoPushToken));
            List<PushDevice> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM push_devices WHERE expo_push_token = @expo_push_token;",
                cmd => MysqlCommandHelper.Add(cmd, "@expo_push_token", expoPushToken), FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<PushDevice> UpdateAsync(PushDevice device, CancellationToken token = default)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            device.LastUpdateUtc = DateTime.UtcNow;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Update, cmd => Bind(cmd, device), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return device;
        }

        /// <inheritdoc />
        public async Task<bool> SetActiveAsync(string id, bool active, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            int updated = 0;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                updated = await MysqlCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE push_devices SET active = @active, last_update_utc = @now WHERE id = @id;",
                    cmd =>
                    {
                        MysqlCommandHelper.Add(cmd, "@active", active);
                        MysqlCommandHelper.AddDate(cmd, "@now", DateTime.UtcNow);
                        MysqlCommandHelper.Add(cmd, "@id", id);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return updated > 0;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            int deleted = 0;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                deleted = await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM push_devices WHERE id = @id;",
                    cmd => MysqlCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted > 0;
        }

        /// <inheritdoc />
        public async Task<int> DeleteByUserAsync(string tenantId, string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            int deleted = 0;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                deleted = await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM push_devices WHERE tenant_id = @tenant_id AND user_id = @user_id;",
                    cmd =>
                    {
                        MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                        MysqlCommandHelper.Add(cmd, "@user_id", userId);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted;
        }

        /// <inheritdoc />
        public async Task<int> DeleteByTenantAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            int deleted = 0;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                deleted = await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM push_devices WHERE tenant_id = @tenant_id;",
                    cmd => MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId), token).ConfigureAwait(false);
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

            return await MysqlCommandHelper.QueryAsync(_ConnectionString, sql.ToString(), cmd =>
            {
                if (query.TenantId != null) MysqlCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (query.UserId != null) MysqlCommandHelper.Add(cmd, "@user_id", query.UserId);
                if (query.ActiveOnly) MysqlCommandHelper.Add(cmd, "@active", true);
            }, FromReader, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Bind(MySqlCommand cmd, PushDevice device)
        {
            MysqlCommandHelper.Add(cmd, "@id", device.Id);
            MysqlCommandHelper.Add(cmd, "@tenant_id", device.TenantId);
            MysqlCommandHelper.Add(cmd, "@user_id", device.UserId);
            MysqlCommandHelper.Add(cmd, "@platform", device.Platform.ToString());
            MysqlCommandHelper.Add(cmd, "@expo_push_token", device.ExpoPushToken);
            MysqlCommandHelper.Add(cmd, "@device_name", device.DeviceName);
            MysqlCommandHelper.Add(cmd, "@app_version", device.AppVersion);
            MysqlCommandHelper.Add(cmd, "@locale", device.Locale);
            MysqlCommandHelper.Add(cmd, "@categories", PushDeviceCategoriesColumn.Format(device.Categories));
            MysqlCommandHelper.Add(cmd, "@active", device.Active);
            MysqlCommandHelper.AddDate(cmd, "@created_utc", device.CreatedUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_seen_utc", device.LastSeenUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", device.LastUpdateUtc);
        }

        private static PushDevice FromReader(MySqlDataReader reader)
        {
            PushDevice device = new PushDevice();
            device.Id = reader["id"].ToString()!;
            device.TenantId = MysqlCommandHelper.ReadString(reader["tenant_id"]);
            device.UserId = MysqlCommandHelper.ReadString(reader["user_id"]);
            device.Platform = MysqlCommandHelper.ReadEnum(reader["platform"], PushPlatformEnum.Ios);
            device.ExpoPushToken = reader["expo_push_token"]?.ToString() ?? String.Empty;
            device.DeviceName = MysqlCommandHelper.ReadString(reader["device_name"]);
            device.AppVersion = MysqlCommandHelper.ReadString(reader["app_version"]);
            device.Locale = MysqlCommandHelper.ReadString(reader["locale"]);
            device.Categories = PushDeviceCategoriesColumn.Parse(MysqlCommandHelper.ReadString(reader["categories"]));
            device.Active = MysqlCommandHelper.ReadBool(reader["active"], true);
            device.CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]);
            device.LastSeenUtc = MysqlCommandHelper.ReadDate(reader["last_seen_utc"]);
            device.LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return device;
        }

        #endregion
    }
}
