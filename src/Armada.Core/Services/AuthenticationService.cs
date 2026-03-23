namespace Armada.Core.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;

    /// <summary>
    /// Service for authenticating API requests via bearer tokens, session tokens, or API keys.
    /// </summary>
    public class AuthenticationService : IAuthenticationService
    {
        #region Private-Members

        private readonly string _Header = "[AuthenticationService] ";
        private readonly DatabaseDriver _Database;
        private readonly ISessionTokenService _SessionTokenService;
        private readonly ArmadaSettings _Settings;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="sessionTokenService">Session token service.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="logging">Logging module.</param>
        public AuthenticationService(
            DatabaseDriver database,
            ISessionTokenService sessionTokenService,
            ArmadaSettings settings,
            LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _SessionTokenService = sessionTokenService ?? throw new ArgumentNullException(nameof(sessionTokenService));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<AuthContext> AuthenticateAsync(
            string? authorizationHeader,
            string? sessionTokenHeader,
            string? apiKeyHeader,
            CancellationToken token = default)
        {
            // 1. Check Bearer token (canonical auth path)
            if (!string.IsNullOrEmpty(authorizationHeader) && authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                string bearerToken = authorizationHeader.Substring(7).Trim();
                if (!string.IsNullOrEmpty(bearerToken))
                {
                    AuthContext? bearerCtx = await AuthenticateByBearerTokenAsync(bearerToken, token).ConfigureAwait(false);
                    if (bearerCtx != null) return bearerCtx;
                }
            }

            // 2. Check session token (X-Token)
            if (!string.IsNullOrEmpty(sessionTokenHeader))
            {
                AuthContext? sessionCtx = await AuthenticateBySessionTokenAsync(sessionTokenHeader, token).ConfigureAwait(false);
                if (sessionCtx != null) return sessionCtx;
            }

            // 3. Check API key (X-Api-Key, deprecated but supported)
            if (!string.IsNullOrEmpty(apiKeyHeader) && !string.IsNullOrEmpty(_Settings.ApiKey))
            {
                if (apiKeyHeader == _Settings.ApiKey)
                {
                    return AuthContext.Authenticated(
                        Constants.SystemTenantId,
                        Constants.SystemUserId,
                        true,
                        true,
                        "ApiKey");
                }
            }

            // 4. Unauthenticated
            return new AuthContext();
        }

        /// <inheritdoc />
        public async Task<AuthContext> AuthenticateWithCredentialsAsync(
            string tenantId,
            string email,
            string password,
            CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
                return new AuthContext();

            try
            {
                UserMaster? user = await _Database.Users.ReadByEmailAsync(tenantId, email, token).ConfigureAwait(false);
                if (user == null) return new AuthContext();
                if (!user.Active) return new AuthContext();
                if (!user.VerifyPassword(password)) return new AuthContext();

                // Verify tenant is active
                TenantMetadata? tenant = await _Database.Tenants.ReadAsync(tenantId, token).ConfigureAwait(false);
                if (tenant == null || !tenant.Active) return new AuthContext();

                return AuthContext.Authenticated(tenantId, user.Id, user.IsAdmin, user.IsAdmin || user.IsTenantAdmin, "Credentials");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "credential authentication failed: " + ex.Message);
                return new AuthContext();
            }
        }

        #endregion

        #region Private-Methods

        private async Task<AuthContext?> AuthenticateByBearerTokenAsync(string bearerToken, CancellationToken token)
        {
            try
            {
                Credential? credential = await _Database.Credentials.ReadByBearerTokenAsync(bearerToken, token).ConfigureAwait(false);
                if (credential == null || !credential.Active) return null;

                UserMaster? user = await _Database.Users.ReadByIdAsync(credential.UserId, token).ConfigureAwait(false);
                if (user == null || !user.Active) return null;

                TenantMetadata? tenant = await _Database.Tenants.ReadAsync(credential.TenantId, token).ConfigureAwait(false);
                if (tenant == null || !tenant.Active) return null;

                return AuthContext.Authenticated(credential.TenantId, credential.UserId, user.IsAdmin, user.IsAdmin || user.IsTenantAdmin, "Bearer");
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "bearer token authentication failed: " + ex.Message);
                return null;
            }
        }

        private async Task<AuthContext?> AuthenticateBySessionTokenAsync(string encryptedToken, CancellationToken token)
        {
            try
            {
                AuthContext? ctx = _SessionTokenService.ValidateToken(encryptedToken);
                if (ctx == null) return null;

                // Verify user is still active
                UserMaster? user = await _Database.Users.ReadByIdAsync(ctx.UserId!, token).ConfigureAwait(false);
                if (user == null || !user.Active) return null;

                // Verify tenant is still active
                TenantMetadata? tenant = await _Database.Tenants.ReadAsync(ctx.TenantId!, token).ConfigureAwait(false);
                if (tenant == null || !tenant.Active) return null;

                ctx.IsAdmin = user.IsAdmin;
                ctx.IsTenantAdmin = user.IsAdmin || user.IsTenantAdmin;
                return ctx;
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "session token authentication failed: " + ex.Message);
                return null;
            }
        }

        #endregion
    }
}
