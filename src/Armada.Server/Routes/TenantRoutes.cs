namespace Armada.Server.Routes
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using SwiftStack;
    using SwiftStack.Rest;
    using Armada.Core;
    using ArmadaConstants = Armada.Core.Constants;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// REST API routes for tenant management.
    /// </summary>
    public class TenantRoutes
    {
        private readonly DatabaseDriver _database;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="jsonOptions">JSON serializer options.</param>
        public TenantRoutes(DatabaseDriver database, JsonSerializerOptions jsonOptions)
        {
            _database = database;
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
            // Tenant CRUD
            app.Rest.Get("/api/v1/tenants", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                EnumerationResult<TenantMetadata> result = await _database.Tenants.EnumerateAsync(query).ConfigureAwait(false);
                // ten_system is excluded at the SQL level (WHERE id != 'ten_system') in all backends
                return (object)result;
            },
            api => api.WithTag("Tenants").WithSummary("List tenants (admin only)"));

            app.Rest.Post("/api/v1/tenants", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string body = req.Http.Request.DataAsString;
                TenantMetadata? tenant = JsonSerializer.Deserialize<TenantMetadata>(body, _jsonOptions);
                if (tenant == null) { req.Http.Response.StatusCode = 400; return (object)new { Error = "Invalid request body" }; }
                tenant.IsProtected = false;
                tenant = await _database.Tenants.CreateAsync(tenant).ConfigureAwait(false);
                await SeedDefaultTenantAdminAsync(tenant).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                return (object)tenant;
            },
            api => api.WithTag("Tenants").WithSummary("Create tenant (admin only)"));

            app.Rest.Get("/api/v1/tenants/{id}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string id = req.Parameters["id"];
                if (!ctx.IsAdmin && ctx.TenantId != id) { req.Http.Response.StatusCode = 404; return (object)new { Error = "Not found" }; }
                TenantMetadata? tenant = await _database.Tenants.ReadAsync(id).ConfigureAwait(false);
                if (tenant == null) { req.Http.Response.StatusCode = 404; return (object)new { Error = "Not found" }; }
                return (object)tenant;
            },
            api => api.WithTag("Tenants").WithSummary("Get tenant by ID"));

            app.Rest.Put("/api/v1/tenants/{id}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string body = req.Http.Request.DataAsString;
                TenantMetadata? tenant = JsonSerializer.Deserialize<TenantMetadata>(body, _jsonOptions);
                if (tenant == null) { req.Http.Response.StatusCode = 400; return (object)new { Error = "Invalid request body" }; }
                TenantMetadata? existing = await _database.Tenants.ReadAsync(req.Parameters["id"]).ConfigureAwait(false);
                if (existing == null) { req.Http.Response.StatusCode = 404; return (object)new { Error = "Not found" }; }
                tenant.Id = req.Parameters["id"];
                tenant.CreatedUtc = existing.CreatedUtc;
                tenant.IsProtected = existing.IsProtected;
                tenant = await _database.Tenants.UpdateAsync(tenant).ConfigureAwait(false);
                return (object)tenant;
            },
            api => api.WithTag("Tenants").WithSummary("Update tenant (admin only)"));

            app.Rest.Delete("/api/v1/tenants/{id}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string id = req.Parameters["id"];
                TenantMetadata? tenant = await _database.Tenants.ReadAsync(id).ConfigureAwait(false);
                if (tenant == null) { req.Http.Response.StatusCode = 404; return (object)new { Error = "Not found" }; }
                if (tenant.IsProtected) { req.Http.Response.StatusCode = 403; return (object)new { Error = "Protected resources cannot be deleted directly" }; }
                await DeleteTenantCascadeAsync(id).ConfigureAwait(false);
                return (object)new { Success = true };
            },
            api => api.WithTag("Tenants").WithSummary("Delete tenant (admin only)"));

            // User CRUD
            app.Rest.Get("/api/v1/users", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                EnumerationResult<UserMaster> result;
                if (ctx.IsAdmin)
                {
                    result = await _database.Users.EnumerateAsync(query).ConfigureAwait(false);
                }
                else if (ctx.IsTenantAdmin)
                {
                    result = await _database.Users.EnumerateAsync(ctx.TenantId!, query).ConfigureAwait(false);
                }
                else
                {
                    UserMaster? self = await _database.Users.ReadAsync(ctx.TenantId!, ctx.UserId!).ConfigureAwait(false);
                    result = EnumerationResult<UserMaster>.Create(query, self != null ? new List<UserMaster> { self } : new List<UserMaster>(), self != null ? 1 : 0);
                }
                result.Objects = result.Objects.Select(u => UserMaster.Redact(u)).ToList();
                return (object)result;
            },
            api => api.WithTag("Users").WithSummary("List users"));

            app.Rest.Post("/api/v1/users", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string body = req.Http.Request.DataAsString;
                UserUpsertRequest? userRequest = JsonSerializer.Deserialize<UserUpsertRequest>(body, _jsonOptions);
                if (userRequest == null || string.IsNullOrWhiteSpace(userRequest.Email))
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new { Error = "Invalid request body" };
                }
                string? passwordHash = ResolvePasswordHash(userRequest, null, true);
                if (passwordHash == null)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new { Error = "Password is required" };
                }
                UserMaster user = new UserMaster
                {
                    Email = userRequest.Email,
                    FirstName = userRequest.FirstName,
                    LastName = userRequest.LastName,
                    IsAdmin = userRequest.IsAdmin,
                    IsTenantAdmin = userRequest.IsTenantAdmin,
                    TenantId = string.IsNullOrWhiteSpace(userRequest.TenantId) ? ArmadaConstants.DefaultTenantId : userRequest.TenantId,
                    Active = userRequest.Active,
                    PasswordSha256 = passwordHash
                };
                user.IsProtected = false;
                if (!ctx.IsAdmin)
                {
                    user.TenantId = ctx.TenantId!;
                    user.IsAdmin = false;
                }
                if (!ctx.IsAdmin && !ctx.IsTenantAdmin)
                {
                    req.Http.Response.StatusCode = 403;
                    return (object)new { Error = "Forbidden" };
                }
                user = await _database.Users.CreateAsync(user).ConfigureAwait(false);
                Credential credential = new Credential(user.TenantId, user.Id)
                {
                    Name = $"Credential for {user.Email}",
                    IsProtected = false,
                };
                await _database.Credentials.CreateAsync(credential).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                return (object)UserMaster.Redact(user);
            },
            api => api.WithTag("Users").WithSummary("Create user (admin only)"));

            app.Rest.Get("/api/v1/users/{id}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string id = req.Parameters["id"];
                bool canReadAnyInTenant = ctx.IsAdmin || ctx.IsTenantAdmin;
                if (!canReadAnyInTenant && ctx.UserId != id) { req.Http.Response.StatusCode = 404; return (object)new { Error = "Not found" }; }
                UserMaster? user = ctx.IsAdmin
                    ? await _database.Users.ReadByIdAsync(id).ConfigureAwait(false)
                    : await _database.Users.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false);
                if (user == null) { req.Http.Response.StatusCode = 404; return (object)new { Error = "Not found" }; }
                return (object)UserMaster.Redact(user);
            },
            api => api.WithTag("Users").WithSummary("Get user by ID"));

            app.Rest.Put("/api/v1/users/{id}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string body = req.Http.Request.DataAsString;
                UserUpsertRequest? userRequest = JsonSerializer.Deserialize<UserUpsertRequest>(body, _jsonOptions);
                if (userRequest == null || string.IsNullOrWhiteSpace(userRequest.Email))
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new { Error = "Invalid request body" };
                }
                UserMaster? existing = await _database.Users.ReadByIdAsync(req.Parameters["id"]).ConfigureAwait(false);
                if (existing == null) { req.Http.Response.StatusCode = 404; return (object)new { Error = "Not found" }; }
                if (!ctx.IsAdmin && existing.TenantId != ctx.TenantId) { req.Http.Response.StatusCode = 404; return (object)new { Error = "Not found" }; }
                if (!ctx.IsAdmin && !ctx.IsTenantAdmin && existing.Id != ctx.UserId) { req.Http.Response.StatusCode = 404; return (object)new { Error = "Not found" }; }
                UserMaster user = new UserMaster
                {
                    Id = req.Parameters["id"],
                    Email = userRequest.Email,
                    FirstName = userRequest.FirstName,
                    LastName = userRequest.LastName,
                    IsAdmin = userRequest.IsAdmin,
                    IsTenantAdmin = userRequest.IsTenantAdmin,
                    TenantId = existing.TenantId,
                    Active = userRequest.Active,
                    PasswordSha256 = ResolvePasswordHash(userRequest, existing.PasswordSha256, false) ?? existing.PasswordSha256
                };
                user.TenantId = existing.TenantId;
                user.CreatedUtc = existing.CreatedUtc;
                user.IsProtected = existing.IsProtected;
                if (!ctx.IsAdmin)
                {
                    user.IsAdmin = existing.IsAdmin;
                }
                if (!ctx.IsAdmin && !ctx.IsTenantAdmin)
                {
                    user.IsTenantAdmin = existing.IsTenantAdmin;
                    user.Active = existing.Active;
                }
                user = await _database.Users.UpdateAsync(user).ConfigureAwait(false);
                return (object)UserMaster.Redact(user);
            },
            api => api.WithTag("Users").WithSummary("Update user (admin only)"));

            app.Rest.Delete("/api/v1/users/{id}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string id = req.Parameters["id"];
                UserMaster? user = await _database.Users.ReadByIdAsync(id).ConfigureAwait(false);
                if (user == null) { req.Http.Response.StatusCode = 404; return (object)new { Error = "Not found" }; }
                if (!ctx.IsAdmin && (!ctx.IsTenantAdmin || user.TenantId != ctx.TenantId)) { req.Http.Response.StatusCode = 403; return (object)new { Error = "Forbidden" }; }
                if (user.IsProtected) { req.Http.Response.StatusCode = 403; return (object)new { Error = "Protected resources cannot be deleted directly" }; }
                await DeleteUserCascadeAsync(user.TenantId, id).ConfigureAwait(false);
                return (object)new { Success = true };
            },
            api => api.WithTag("Users").WithSummary("Delete user (admin only)"));

            // Credential CRUD
            app.Rest.Get("/api/v1/credentials", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                EnumerationQuery query = new EnumerationQuery();
                query.ApplyQuerystringOverrides(key => req.Query.GetValueOrDefault(key));
                if (ctx.IsAdmin)
                {
                    EnumerationResult<Credential> result = await _database.Credentials.EnumerateAsync(query).ConfigureAwait(false);
                    return (object)result;
                }
                else if (ctx.IsTenantAdmin)
                {
                    EnumerationResult<Credential> result = await _database.Credentials.EnumerateAsync(ctx.TenantId!, query).ConfigureAwait(false);
                    return (object)result;
                }
                else
                {
                    EnumerationResult<Credential> result = await _database.Credentials.EnumerateByUserAsync(ctx.TenantId!, ctx.UserId!, query).ConfigureAwait(false);
                    return (object)result;
                }
            },
            api => api.WithTag("Credentials").WithSummary("List credentials"));

            app.Rest.Post("/api/v1/credentials", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string body = req.Http.Request.DataAsString;
                Credential? cred = JsonSerializer.Deserialize<Credential>(body, _jsonOptions);
                if (cred == null) { req.Http.Response.StatusCode = 400; return (object)new { Error = "Invalid request body" }; }
                cred.IsProtected = false;
                if (!ctx.IsAdmin)
                {
                    cred.TenantId = ctx.TenantId!;
                    if (!ctx.IsTenantAdmin) cred.UserId = ctx.UserId!;
                }
                if (!ctx.IsAdmin && ctx.IsTenantAdmin)
                {
                    UserMaster? owner = await _database.Users.ReadAsync(ctx.TenantId!, cred.UserId).ConfigureAwait(false);
                    if (owner == null) { req.Http.Response.StatusCode = 400; return (object)new { Error = "User not found in tenant" }; }
                }
                cred = await _database.Credentials.CreateAsync(cred).ConfigureAwait(false);
                req.Http.Response.StatusCode = 201;
                return (object)cred;
            },
            api => api.WithTag("Credentials").WithSummary("Create credential"));

            app.Rest.Get("/api/v1/credentials/{id}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string id = req.Parameters["id"];
                Credential? cred = ctx.IsAdmin ? await _database.Credentials.ReadByIdAsync(id).ConfigureAwait(false) : await _database.Credentials.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false);
                if (cred == null || (!ctx.IsAdmin && !ctx.IsTenantAdmin && cred.UserId != ctx.UserId)) { req.Http.Response.StatusCode = 404; return (object)new { Error = "Not found" }; }
                return (object)cred;
            },
            api => api.WithTag("Credentials").WithSummary("Get credential by ID"));

            app.Rest.Put("/api/v1/credentials/{id}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string body = req.Http.Request.DataAsString;
                Credential? cred = JsonSerializer.Deserialize<Credential>(body, _jsonOptions);
                if (cred == null) { req.Http.Response.StatusCode = 400; return (object)new { Error = "Invalid request body" }; }
                Credential? existing = await _database.Credentials.ReadByIdAsync(req.Parameters["id"]).ConfigureAwait(false);
                if (existing == null) { req.Http.Response.StatusCode = 404; return (object)new { Error = "Not found" }; }
                if (!ctx.IsAdmin && existing.TenantId != ctx.TenantId) { req.Http.Response.StatusCode = 404; return (object)new { Error = "Not found" }; }
                if (!ctx.IsAdmin && !ctx.IsTenantAdmin && existing.UserId != ctx.UserId) { req.Http.Response.StatusCode = 404; return (object)new { Error = "Not found" }; }
                cred.Id = req.Parameters["id"];
                cred.TenantId = existing.TenantId;
                cred.UserId = existing.UserId;
                cred.CreatedUtc = existing.CreatedUtc;
                cred.IsProtected = existing.IsProtected;
                cred = await _database.Credentials.UpdateAsync(cred).ConfigureAwait(false);
                return (object)cred;
            },
            api => api.WithTag("Credentials").WithSummary("Update credential (admin only)"));

            app.Rest.Delete("/api/v1/credentials/{id}", async (AppRequest req) =>
            {
                AuthContext ctx = await authenticate(req.Http).ConfigureAwait(false);
                if (!authz.IsAuthorized(ctx, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery))
                {
                    req.Http.Response.StatusCode = ctx.IsAuthenticated ? 403 : 401;
                    return (object)new { Error = ctx.IsAuthenticated ? "Forbidden" : "Unauthorized" };
                }
                string id = req.Parameters["id"];
                Credential? cred = ctx.IsAdmin ? await _database.Credentials.ReadByIdAsync(id).ConfigureAwait(false) : await _database.Credentials.ReadAsync(ctx.TenantId!, id).ConfigureAwait(false);
                if (cred == null || (!ctx.IsAdmin && !ctx.IsTenantAdmin && cred.UserId != ctx.UserId)) { req.Http.Response.StatusCode = 404; return (object)new { Error = "Not found" }; }
                if (cred.IsProtected) { req.Http.Response.StatusCode = 403; return (object)new { Error = "Protected resources cannot be deleted directly" }; }
                await _database.Credentials.DeleteAsync(cred.TenantId, id).ConfigureAwait(false);
                return (object)new { Success = true };
            },
            api => api.WithTag("Credentials").WithSummary("Delete credential"));
        }

        private async Task SeedDefaultTenantAdminAsync(TenantMetadata tenant)
        {
            UserMaster? existing = await _database.Users.ReadByEmailAsync(tenant.Id, ArmadaConstants.DefaultUserEmail).ConfigureAwait(false);
            if (existing != null) return;

            UserMaster user = new UserMaster(tenant.Id, ArmadaConstants.DefaultUserEmail, ArmadaConstants.DefaultUserPassword)
            {
                IsAdmin = false,
                IsTenantAdmin = true,
                IsProtected = true
            };

            await _database.Users.CreateAsync(user).ConfigureAwait(false);

            Credential credential = new Credential(tenant.Id, user.Id)
            {
                Name = "Default Admin Credential",
                IsProtected = true
            };

            await _database.Credentials.CreateAsync(credential).ConfigureAwait(false);
        }

        private static string? ResolvePasswordHash(UserUpsertRequest request, string? existingHash, bool requirePassword)
        {
            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                return UserMaster.ComputePasswordHash(request.Password);
            }

            if (!string.IsNullOrWhiteSpace(request.PasswordSha256) && request.PasswordSha256 != "********")
            {
                return request.PasswordSha256;
            }

            if (!string.IsNullOrWhiteSpace(existingHash))
            {
                return existingHash;
            }

            return requirePassword ? null : existingHash;
        }

        private async Task DeleteTenantCascadeAsync(string tenantId)
        {
            foreach (Signal signal in await _database.Signals.EnumerateAsync(tenantId).ConfigureAwait(false))
                await _database.Signals.DeleteAsync(tenantId, signal.Id).ConfigureAwait(false);
            foreach (ArmadaEvent evt in await _database.Events.EnumerateAsync(tenantId).ConfigureAwait(false))
                await _database.Events.DeleteAsync(tenantId, evt.Id).ConfigureAwait(false);
            foreach (MergeEntry entry in await _database.MergeEntries.EnumerateAsync(tenantId).ConfigureAwait(false))
                await _database.MergeEntries.DeleteAsync(tenantId, entry.Id).ConfigureAwait(false);
            foreach (Mission mission in await _database.Missions.EnumerateAsync(tenantId).ConfigureAwait(false))
                await _database.Missions.DeleteAsync(tenantId, mission.Id).ConfigureAwait(false);
            foreach (Dock dock in await _database.Docks.EnumerateAsync(tenantId).ConfigureAwait(false))
                await _database.Docks.DeleteAsync(tenantId, dock.Id).ConfigureAwait(false);
            foreach (Voyage voyage in await _database.Voyages.EnumerateAsync(tenantId).ConfigureAwait(false))
                await _database.Voyages.DeleteAsync(voyage.Id).ConfigureAwait(false);
            foreach (Vessel vessel in await _database.Vessels.EnumerateAsync(tenantId).ConfigureAwait(false))
                await _database.Vessels.DeleteAsync(tenantId, vessel.Id).ConfigureAwait(false);
            foreach (Captain captain in await _database.Captains.EnumerateAsync(tenantId).ConfigureAwait(false))
                await _database.Captains.DeleteAsync(tenantId, captain.Id).ConfigureAwait(false);
            foreach (Fleet fleet in await _database.Fleets.EnumerateAsync(tenantId).ConfigureAwait(false))
                await _database.Fleets.DeleteAsync(tenantId, fleet.Id).ConfigureAwait(false);
            foreach (Credential cred in await _database.Credentials.EnumerateAsync(tenantId).ConfigureAwait(false))
                await _database.Credentials.DeleteAsync(tenantId, cred.Id).ConfigureAwait(false);
            foreach (UserMaster user in await _database.Users.EnumerateAsync(tenantId).ConfigureAwait(false))
                await _database.Users.DeleteAsync(tenantId, user.Id).ConfigureAwait(false);
            await _database.Tenants.DeleteAsync(tenantId).ConfigureAwait(false);
        }

        private async Task DeleteUserCascadeAsync(string tenantId, string userId)
        {
            foreach (Signal signal in (await _database.Signals.EnumerateAsync(tenantId).ConfigureAwait(false)).Where(x => x.UserId == userId))
                await _database.Signals.DeleteAsync(tenantId, signal.Id).ConfigureAwait(false);
            foreach (ArmadaEvent evt in (await _database.Events.EnumerateAsync(tenantId).ConfigureAwait(false)).Where(x => x.UserId == userId))
                await _database.Events.DeleteAsync(tenantId, evt.Id).ConfigureAwait(false);
            foreach (MergeEntry entry in (await _database.MergeEntries.EnumerateAsync(tenantId).ConfigureAwait(false)).Where(x => x.UserId == userId))
                await _database.MergeEntries.DeleteAsync(tenantId, entry.Id).ConfigureAwait(false);
            foreach (Mission mission in (await _database.Missions.EnumerateAsync(tenantId).ConfigureAwait(false)).Where(x => x.UserId == userId))
                await _database.Missions.DeleteAsync(tenantId, mission.Id).ConfigureAwait(false);
            foreach (Dock dock in (await _database.Docks.EnumerateAsync(tenantId).ConfigureAwait(false)).Where(x => x.UserId == userId))
                await _database.Docks.DeleteAsync(tenantId, dock.Id).ConfigureAwait(false);
            foreach (Voyage voyage in (await _database.Voyages.EnumerateAsync(tenantId).ConfigureAwait(false)).Where(x => x.UserId == userId))
                await _database.Voyages.DeleteAsync(voyage.Id).ConfigureAwait(false);
            foreach (Vessel vessel in (await _database.Vessels.EnumerateAsync(tenantId).ConfigureAwait(false)).Where(x => x.UserId == userId))
                await _database.Vessels.DeleteAsync(tenantId, vessel.Id).ConfigureAwait(false);
            foreach (Captain captain in (await _database.Captains.EnumerateAsync(tenantId).ConfigureAwait(false)).Where(x => x.UserId == userId))
                await _database.Captains.DeleteAsync(tenantId, captain.Id).ConfigureAwait(false);
            foreach (Fleet fleet in (await _database.Fleets.EnumerateAsync(tenantId).ConfigureAwait(false)).Where(x => x.UserId == userId))
                await _database.Fleets.DeleteAsync(tenantId, fleet.Id).ConfigureAwait(false);
            foreach (Credential cred in await _database.Credentials.EnumerateByUserAsync(tenantId, userId).ConfigureAwait(false))
                await _database.Credentials.DeleteAsync(tenantId, cred.Id).ConfigureAwait(false);
            await _database.Users.DeleteAsync(tenantId, userId).ConfigureAwait(false);
        }
    }
}
