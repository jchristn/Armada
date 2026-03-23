namespace Armada.Server.Routes
{
    using System.Text.Json;
    using SwiftStack;
    using SwiftStack.Rest;
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

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="sessionTokenService">Session token service.</param>
        /// <param name="authenticationService">Authentication service.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="jsonOptions">JSON serializer options.</param>
        public AuthRoutes(
            ISessionTokenService sessionTokenService,
            IAuthenticationService authenticationService,
            DatabaseDriver database,
            ArmadaSettings settings,
            JsonSerializerOptions jsonOptions)
        {
            _sessionTokenService = sessionTokenService;
            _authenticationService = authenticationService;
            _database = database;
            _settings = settings;
            _jsonOptions = jsonOptions;
        }

        /// <summary>
        /// Register routes with the application.
        /// </summary>
        /// <param name="app">SwiftStack application.</param>
        /// <param name="authenticate">Authentication middleware.</param>
        /// <param name="authz">Authorization service.</param>
        public void Register(
            SwiftStackApp app,
            Func<WatsonWebserver.Core.HttpContextBase, Task<AuthContext>> authenticate,
            IAuthorizationService authz)
        {
            // Authentication
            app.Rest.Post("/api/v1/authenticate", async (AppRequest req) =>
            {
                string body = req.Http.Request.DataAsString;
                AuthenticateRequest? authReq = null;
                if (!string.IsNullOrEmpty(body))
                    authReq = JsonSerializer.Deserialize<AuthenticateRequest>(body, _jsonOptions);

                // Try header-based auth first
                AuthContext headerCtx = await authenticate(req.Http).ConfigureAwait(false);
                if (headerCtx.IsAuthenticated)
                {
                    AuthenticateResult result = _sessionTokenService.CreateToken(headerCtx.TenantId!, headerCtx.UserId!);
                    return (object)result;
                }

                // Try email/password
                if (authReq != null && !string.IsNullOrEmpty(authReq.TenantId) && !string.IsNullOrEmpty(authReq.Email) && !string.IsNullOrEmpty(authReq.Password))
                {
                    AuthContext credCtx = await _authenticationService.AuthenticateWithCredentialsAsync(authReq.TenantId, authReq.Email, authReq.Password).ConfigureAwait(false);
                    if (credCtx.IsAuthenticated)
                    {
                        AuthenticateResult result = _sessionTokenService.CreateToken(credCtx.TenantId!, credCtx.UserId!);
                        return (object)result;
                    }
                }

                req.Http.Response.StatusCode = 401;
                return (object)new AuthenticateResult { Success = false };
            },
            api => api.WithTag("Authentication").WithSummary("Authenticate and get session token"));

            // WhoAmI
            app.Rest.Get("/api/v1/whoami", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }

                TenantMetadata? tenant = await _database.Tenants.ReadAsync(ctx.TenantId!).ConfigureAwait(false);
                UserMaster? user = await _database.Users.ReadByIdAsync(ctx.UserId!).ConfigureAwait(false);

                return (object)new WhoAmIResult
                {
                    Tenant = tenant,
                    User = user != null ? UserMaster.Redact(user) : null
                };
            },
            api => api.WithTag("Authentication").WithSummary("Get current identity"));

            // Tenant Lookup
            app.Rest.Post("/api/v1/tenants/lookup", async (AppRequest req) =>
            {
                string body = req.Http.Request.DataAsString;
                TenantLookupRequest? lookupReq = JsonSerializer.Deserialize<TenantLookupRequest>(body, _jsonOptions);
                if (lookupReq == null || string.IsNullOrEmpty(lookupReq.Email))
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new { Error = "Email is required" };
                }

                List<UserMaster> users = await _database.Users.ReadByEmailAnyTenantAsync(lookupReq.Email).ConfigureAwait(false);
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
            app.Rest.Post("/api/v1/onboarding", async (AppRequest req) =>
            {
                if (!_settings.AllowSelfRegistration)
                {
                    req.Http.Response.StatusCode = 403;
                    return (object)new OnboardingResult { Success = false, ErrorMessage = "Self-registration is disabled" };
                }

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
    }
}
