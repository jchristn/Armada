namespace Armada.Server.Routes
{
    using System.Text.Json;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using Armada.Server;
    using Armada.Core;
    using ArmadaConstants = Armada.Core.Constants;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;

    /// <summary>
    /// REST API routes for authentication management.
    /// </summary>
    public class AuthRoutes
    {
        private readonly ISessionTokenService _sessionTokenService;
        private readonly IAuthenticationService _authenticationService;
        private readonly DatabaseDriver _database;
        private readonly ArmadaSettings _settings;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly Armada.Core.Services.DefaultCredentialService? _defaults;
        private readonly Armada.Core.Services.LoginRateLimiter? _rateLimiter;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="sessionTokenService">Session token service.</param>
        /// <param name="authenticationService">Authentication service.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="jsonOptions">JSON serializer options.</param>
        /// <param name="defaults">Default credential service (password change retires the seeded token; whoami reports defaults in use).</param>
        /// <param name="rateLimiter">Login rate limiter (null disables rate limiting on these routes).</param>
        public AuthRoutes(
            ISessionTokenService sessionTokenService,
            IAuthenticationService authenticationService,
            DatabaseDriver database,
            ArmadaSettings settings,
            JsonSerializerOptions jsonOptions,
            Armada.Core.Services.DefaultCredentialService? defaults = null,
            Armada.Core.Services.LoginRateLimiter? rateLimiter = null)
        {
            _defaults = defaults;
            _rateLimiter = rateLimiter;
            _sessionTokenService = sessionTokenService;
            _authenticationService = authenticationService;
            _database = database;
            _settings = settings;
            _jsonOptions = jsonOptions;
        }

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        /// <param name="app">Webserver.</param>
        /// <param name="authenticate">Authentication middleware.</param>
        /// <param name="authz">Authorization service.</param>
        public void Register(
            Webserver app,
            Func<WatsonWebserver.Core.HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            // Authentication
            app.Post("/api/v1/authenticate", async (ApiRequest req) =>
            {
                string body = req.Http.Request.DataAsString;
                AuthenticateRequest? authReq = null;
                if (!string.IsNullOrEmpty(body))
                    authReq = JsonSerializer.Deserialize<AuthenticateRequest>(body, _jsonOptions);

                // Login rate limiting: a locked-out address (guessable credentials: password, bearer token, API key) or a
                // locked-out account gets 429 with Retry-After, even when the credential is right. Session tokens (X-Token)
                // are not guessable and keep working.
                string? address = ClientAddress(req.Http);
                bool passwordLogin = authReq != null && !string.IsNullOrEmpty(authReq.TenantId) && !string.IsNullOrEmpty(authReq.Email) && !string.IsNullOrEmpty(authReq.Password);
                if (_rateLimiter != null)
                {
                    TimeSpan? retryAfter = null;
                    if (passwordLogin) retryAfter = _rateLimiter.CheckPasswordLogin(authReq!.TenantId, authReq.Email, address);
                    else if (PresentsGuessableCredential(req.Http)) retryAfter = _rateLimiter.CheckAddress(address);
                    if (retryAfter != null) return (object)TooManyAttempts(req.Http, retryAfter.Value);
                }

                // Try header-based auth first
                AuthContext headerCtx = await authenticate(req.Http).ConfigureAwait(false);
                if (headerCtx.IsAuthenticated)
                {
                    AuthenticateResult result = _sessionTokenService.CreateToken(headerCtx.TenantId!, headerCtx.UserId!);
                    UserMaster? headerUser = await _database.Users.ReadByIdAsync(headerCtx.UserId!).ConfigureAwait(false);
                    result.PasswordChangeRequired = headerUser != null && headerUser.UsesDefaultPassword();
                    return (object)result;
                }

                // Try email/password
                if (authReq != null && !string.IsNullOrEmpty(authReq.TenantId) && !string.IsNullOrEmpty(authReq.Email) && !string.IsNullOrEmpty(authReq.Password))
                {
                    AuthContext credCtx = await _authenticationService.AuthenticateWithCredentialsAsync(authReq.TenantId, authReq.Email, authReq.Password).ConfigureAwait(false);
                    if (!credCtx.IsAuthenticated) _rateLimiter?.RecordPasswordFailure(authReq.TenantId, authReq.Email, address);
                    if (credCtx.IsAuthenticated)
                    {
                        _rateLimiter?.RecordPasswordSuccess(authReq.TenantId, authReq.Email);
                        AuthenticateResult result = _sessionTokenService.CreateToken(credCtx.TenantId!, credCtx.UserId!);
                        result.PasswordChangeRequired = credCtx.PasswordChangeRequired;
                        return (object)result;
                    }
                }

                req.Http.Response.StatusCode = 401;
                return (object)new AuthenticateResult { Success = false };
            },
            api => api.WithTag("Authentication").WithSummary("Authenticate and get session token"));

            // WhoAmI
            app.Get("/api/v1/whoami", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }

                TenantMetadata? tenant = await _database.Tenants.ReadAsync(ctx.TenantId!).ConfigureAwait(false);
                UserMaster? user = await _database.Users.ReadByIdAsync(ctx.UserId!).ConfigureAwait(false);

                WhoAmIResult whoami = new WhoAmIResult
                {
                    Tenant = tenant,
                    User = user != null ? UserMaster.Redact(user) : null,
                    PasswordChangeRequired = user != null && user.UsesDefaultPassword()
                };
                if (_defaults != null && (ctx.IsAdmin || ctx.IsTenantAdmin))
                {
                    List<string> inUse = await _defaults.GetDefaultsInUseAsync().ConfigureAwait(false);
                    whoami.DefaultCredentialsInUse = inUse.Count > 0;
                }

                return (object)whoami;
            },
            api => api.WithTag("Authentication").WithSummary("Get current identity"));

            // Self-service password change (required for the seeded admin before its session can use the API)
            app.Put<PasswordChangeRequest>("/api/v1/account/password", async (ApiRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return new ApiErrorResponse { Error = ctx.IsAuthenticated ? ApiResultEnum.Forbidden : ApiResultEnum.NotAuthorized, Message = ctx.IsAuthenticated ? "You do not have permission to perform this action" : "Authentication required" };
                }

                PasswordChangeRequest? change = null;
                try { change = JsonSerializer.Deserialize<PasswordChangeRequest>(req.Http.Request.DataAsString, _jsonOptions); } catch (JsonException) { }
                if (change == null || String.IsNullOrEmpty(change.CurrentPassword) || String.IsNullOrEmpty(change.NewPassword))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "CurrentPassword and NewPassword are required" };
                }

                if (change.NewPassword!.Length < PasswordChangeRequest.MinimumLength || String.Equals(change.NewPassword, ArmadaConstants.DefaultUserPassword, StringComparison.Ordinal))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "NewPassword must be at least " + PasswordChangeRequest.MinimumLength + " characters and must not be the default password" };
                }

                if (String.Equals(change.NewPassword, change.CurrentPassword, StringComparison.Ordinal))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "NewPassword must differ from CurrentPassword" };
                }

                UserMaster? user = await _database.Users.ReadByIdAsync(ctx.UserId!).ConfigureAwait(false);
                if (user == null || String.Equals(user.Id, ArmadaConstants.SystemUserId, StringComparison.Ordinal))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "This identity has no password to change" };
                }

                if (!user.VerifyPassword(change.CurrentPassword!))
                {
                    req.Http.Response.StatusCode = 403;
                    return new ApiErrorResponse { Error = ApiResultEnum.Forbidden, Message = "CurrentPassword is incorrect" };
                }

                user.PasswordSha256 = UserMaster.ComputePasswordHash(change.NewPassword);
                user.LastUpdateUtc = DateTime.UtcNow;
                await _database.Users.UpdateAsync(user).ConfigureAwait(false);
                if (_defaults != null) await _defaults.RetireDefaultBearerTokenAsync().ConfigureAwait(false);

                return (object)new WhoAmIResult
                {
                    Tenant = await _database.Tenants.ReadAsync(user.TenantId).ConfigureAwait(false),
                    User = UserMaster.Redact(user),
                    PasswordChangeRequired = false
                };
            },
            api => api
                .WithTag("Authentication")
                .WithSummary("Change the caller's password")
                .WithDescription("Changes the authenticated user's password after verifying the current one. Required before a dashboard session for the seeded admin@armada account (default password) can use the rest of the API. Changing the default admin's password also deactivates the seeded \"default\" bearer token.")
                .WithRequestBody(OpenApiJson.BodyFor<PasswordChangeRequest>("Current and new password", true))
                .WithResponse(200, OpenApiJson.For<WhoAmIResult>("Updated identity"))
                .WithSecurity("ApiKey"));

            // Tenant Lookup
            app.Post("/api/v1/tenants/lookup", async (ApiRequest req) =>
            {
                // Maps an email to its tenants: every request counts against a per-address budget.
                TimeSpan? lookupRetry = _rateLimiter?.CheckAndCountLookup(ClientAddress(req.Http));
                if (lookupRetry != null) return (object)TooManyAttempts(req.Http, lookupRetry.Value);

                string body = req.Http.Request.DataAsString;
                TenantLookupRequest? lookupReq = JsonSerializer.Deserialize<TenantLookupRequest>(body, _jsonOptions);
                if (lookupReq == null || string.IsNullOrEmpty(lookupReq.Email))
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Email is required" };
                }

                List<UserMaster> users = await _database.Users.ReadByEmailAnyTenantAsync(lookupReq.Email.ToLowerInvariant()).ConfigureAwait(false);
                TenantLookupResult result = new TenantLookupResult();
                foreach (UserMaster u in users)
                {
                    if (u.TenantId == ArmadaConstants.SystemTenantId) continue;
                    TenantMetadata? t = await _database.Tenants.ReadAsync(u.TenantId).ConfigureAwait(false);
                    if (t != null && t.Active)
                        result.Tenants.Add(new TenantListEntry(t.Id, t.Name));
                }
                return (object)result;
            },
            api => api.WithTag("Authentication").WithSummary("Look up tenants by email"));

            // Onboarding
            app.Post("/api/v1/onboarding", async (ApiRequest req) =>
            {
                if (!_settings.AllowSelfRegistration)
                {
                    req.Http.Response.StatusCode = 403;
                    return (object)new OnboardingResult { Success = false, ErrorMessage = "Self-registration is disabled" };
                }

                TimeSpan? onboardingRetry = _rateLimiter?.CheckAndCountLookup(ClientAddress(req.Http));
                if (onboardingRetry != null) return (object)TooManyAttempts(req.Http, onboardingRetry.Value);

                string body = req.Http.Request.DataAsString;
                OnboardingRequest? onbReq = JsonSerializer.Deserialize<OnboardingRequest>(body, _jsonOptions);
                if (onbReq == null || string.IsNullOrEmpty(onbReq.TenantId) || string.IsNullOrEmpty(onbReq.Email) || string.IsNullOrEmpty(onbReq.Password))
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new OnboardingResult { Success = false, ErrorMessage = "TenantId, Email, and Password are required" };
                }

                TenantMetadata? tenant = await _database.Tenants.ReadAsync(onbReq.TenantId).ConfigureAwait(false);
                if (tenant == null || !tenant.Active)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new OnboardingResult { Success = false, ErrorMessage = "Tenant not found or inactive" };
                }

                UserMaster? existing = await _database.Users.ReadByEmailAsync(onbReq.TenantId, onbReq.Email).ConfigureAwait(false);
                if (existing != null)
                {
                    req.Http.Response.StatusCode = 409;
                    return (object)new OnboardingResult { Success = false, ErrorMessage = "Email already exists in this tenant" };
                }

                UserMaster newUser = new UserMaster(onbReq.TenantId, onbReq.Email, onbReq.Password);
                newUser.FirstName = onbReq.FirstName;
                newUser.LastName = onbReq.LastName;
                newUser.IsAdmin = false;
                newUser.IsTenantAdmin = false;
                await _database.Users.CreateAsync(newUser).ConfigureAwait(false);

                Credential newCred = new Credential(onbReq.TenantId, newUser.Id);
                await _database.Credentials.CreateAsync(newCred).ConfigureAwait(false);

                return (object)new OnboardingResult
                {
                    Success = true,
                    Tenant = tenant,
                    User = UserMaster.Redact(newUser),
                    Credential = newCred
                };
            },
            api => api.WithTag("Authentication").WithSummary("Self-register a new user"));
        }

        /// <summary>
        /// Client address of a request as seen by the listener (X-Forwarded-For is not trusted).
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>Address, or null.</returns>
        public static string? ClientAddress(HttpContextBase ctx)
        {
            if (ctx == null) return null;
            string? address = ctx.Request.Source?.IpAddress?.ToString();
            return string.IsNullOrEmpty(address) ? null : address;
        }

        /// <summary>
        /// Whether a request presents a guessable credential header (Authorization or X-Api-Key). Session tokens (X-Token)
        /// are encrypted by the server and are not counted.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>True when a guessable credential is presented.</returns>
        public static bool PresentsGuessableCredential(HttpContextBase ctx)
        {
            if (ctx == null) return false;
            return !string.IsNullOrEmpty(ctx.Request.Headers.Get("Authorization")) || !string.IsNullOrEmpty(ctx.Request.Headers.Get("X-Api-Key"));
        }

        /// <summary>
        /// Prepare a 429 response with Retry-After and return its body.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="retryAfter">Remaining lockout.</param>
        /// <returns>Error body.</returns>
        public static ApiErrorResponse TooManyAttempts(HttpContextBase ctx, TimeSpan retryAfter)
        {
            string seconds = Armada.Core.Services.LoginRateLimiter.ToRetryAfterSeconds(retryAfter);
            ctx.Response.StatusCode = 429;
            ctx.Response.Headers.Add("Retry-After", seconds);
            return new ApiErrorResponse { Error = ApiResultEnum.SlowDown, Message = "Too many failed attempts; try again in " + seconds + " seconds" };
        }
    }
}
