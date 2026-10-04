namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Models;

    /// <summary>
    /// Upgrades stored password hashes written by earlier releases (unsalted SHA-256 hex) to the salted PBKDF2 format at
    /// Admiral start. The stored SHA-256 value is itself stretched (see <see cref="PasswordHasher"/>), so no password is
    /// needed and every user can still sign in with the same password. A row that is somehow still legacy afterwards is
    /// upgraded on its next successful login.
    /// </summary>
    public class PasswordHashUpgradeService
    {
        #region Private-Members

        private readonly string _Header = "[PasswordHashUpgradeService] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        public PasswordHashUpgradeService(DatabaseDriver database, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Rewrite every user whose stored hash is not in the PBKDF2 format.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of users upgraded.</returns>
        public async Task<int> UpgradeLegacyHashesAsync(CancellationToken token = default)
        {
            List<UserMaster> legacy = new List<UserMaster>();
            EnumerationQuery query = new EnumerationQuery();
            query.PageSize = 1000;
            query.PageNumber = 1;

            while (true)
            {
                EnumerationResult<UserMaster> page = await _Database.Users.EnumerateAsync(query, token).ConfigureAwait(false);
                if (page.Objects == null || page.Objects.Count == 0) break;
                foreach (UserMaster user in page.Objects)
                {
                    if (!PasswordHasher.IsAdaptiveHash(user.PasswordSha256)) legacy.Add(user);
                }

                if (query.PageNumber >= page.TotalPages) break;
                query.PageNumber = query.PageNumber + 1;
            }

            int upgraded = 0;
            foreach (UserMaster user in legacy)
            {
                try
                {
                    // The database layer stretches any non-PBKDF2 value on write.
                    await _Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
                    upgraded++;
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "could not upgrade the password hash of user " + user.Id + ": " + ex.Message);
                }
            }

            if (upgraded > 0) _Logging.Info(_Header + "upgraded " + upgraded + " legacy password hash(es) to " + PasswordHasher.Scheme);
            return upgraded;
        }

        #endregion
    }
}
