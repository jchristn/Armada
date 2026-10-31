namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using SyslogLogging;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// PostgreSQL implementation of durable coordination lease operations. Provides restart-safe,
    /// multi-instance-safe mutual exclusion via an atomic compare-and-swap on the lease name with
    /// TTL-based takeover, so a crashed holder never blocks progress permanently.
    /// </summary>
    public class CoordinationLeaseMethods : ICoordinationLeaseMethods
    {
        #region Private-Members

#pragma warning disable CS0414
        private string _Header = "[CoordinationLeaseMethods] ";
#pragma warning restore CS0414
        private PostgresqlDatabaseDriver _Driver;
        private DatabaseSettings _Settings;
        private LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">PostgreSQL database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        public CoordinationLeaseMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<bool> TryAcquireAsync(string name, string holder, TimeSpan ttl, string? tenantId = null, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrEmpty(holder)) throw new ArgumentNullException(nameof(holder));

            DateTime now = DateTime.UtcNow;
            DateTime expires = now.Add(ttl);

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = @"INSERT INTO coordination_leases (name, holder, tenant_id, acquired_utc, expires_utc)
                        VALUES (@name, @holder, @tenant_id, @acquired_utc, @expires_utc)
                        ON CONFLICT (name) DO UPDATE SET
                            holder = EXCLUDED.holder,
                            tenant_id = EXCLUDED.tenant_id,
                            acquired_utc = EXCLUDED.acquired_utc,
                            expires_utc = EXCLUDED.expires_utc
                        WHERE coordination_leases.expires_utc <= @now;";
                    cmd.Parameters.AddWithValue("@name", name);
                    cmd.Parameters.AddWithValue("@holder", holder);
                    cmd.Parameters.AddWithValue("@tenant_id", (object?)tenantId ?? DBNull.Value);
                    PostgresqlCommandHelper.AddDate(cmd, "@acquired_utc", now);
                    PostgresqlCommandHelper.AddDate(cmd, "@expires_utc", expires);
                    PostgresqlCommandHelper.AddDate(cmd, "@now", now);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT holder FROM coordination_leases WHERE name = @name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                    if (result == null || result == DBNull.Value) return false;
                    return string.Equals(result.ToString(), holder, StringComparison.Ordinal);
                }
            }
        }

        /// <inheritdoc />
        public async Task<bool> TryRenewAsync(string name, string holder, TimeSpan ttl, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrEmpty(holder)) throw new ArgumentNullException(nameof(holder));

            DateTime now = DateTime.UtcNow;
            DateTime newExpires = now.Add(ttl);

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = @"UPDATE coordination_leases SET expires_utc = @new_expires
                        WHERE name = @name AND holder = @holder AND expires_utc > @now;";
                    cmd.Parameters.AddWithValue("@new_expires", newExpires);
                    cmd.Parameters.AddWithValue("@name", name);
                    cmd.Parameters.AddWithValue("@holder", holder);
                    PostgresqlCommandHelper.AddDate(cmd, "@now", now);
                    int rowsAffected = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    return rowsAffected > 0;
                }
            }
        }

        /// <inheritdoc />
        public async Task ReleaseAsync(string name, string holder, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrEmpty(holder)) throw new ArgumentNullException(nameof(holder));

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "DELETE FROM coordination_leases WHERE name = @name AND holder = @holder;";
                    cmd.Parameters.AddWithValue("@name", name);
                    cmd.Parameters.AddWithValue("@holder", holder);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<CoordinationLease?> ReadAsync(string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM coordination_leases WHERE name = @name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return LeaseFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<int> PurgeExpiredAsync(CancellationToken token = default)
        {
            DateTime now = DateTime.UtcNow;

            using (NpgsqlConnection conn = new NpgsqlConnection(_Settings.GetConnectionString()))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "DELETE FROM coordination_leases WHERE expires_utc <= @now;";
                    PostgresqlCommandHelper.AddDate(cmd, "@now", now);
                    int rowsAffected = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    return rowsAffected;
                }
            }
        }

        #endregion

        #region Private-Methods

        private static CoordinationLease LeaseFromReader(NpgsqlDataReader reader)
        {
            CoordinationLease lease = new CoordinationLease();
            lease.Name = reader["name"].ToString()!;
            lease.Holder = reader["holder"].ToString()!;
            lease.TenantId = NullableString(reader["tenant_id"]);
            lease.AcquiredUtc = DateTime.SpecifyKind((DateTime)reader["acquired_utc"], DateTimeKind.Utc);
            lease.ExpiresUtc = DateTime.SpecifyKind((DateTime)reader["expires_utc"], DateTimeKind.Utc);
            return lease;
        }

        private static string? NullableString(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string str = value.ToString()!;
            return string.IsNullOrEmpty(str) ? null : str;
        }

        #endregion
    }
}
