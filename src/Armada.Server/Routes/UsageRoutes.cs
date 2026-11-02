namespace Armada.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>Bounded, tenant-scoped account usage endpoints.</summary>
    public class UsageRoutes
    {
        private readonly UsageAccessService _Service;
        private readonly JsonSerializerOptions _Options;
        /// <summary>UsageRoutes: account usage contract.</summary>
        public UsageRoutes(UsageAccessService service, JsonSerializerOptions options) { _Service = service; _Options = options; }
        /// <summary>Register: account usage contract.</summary>
        public void Register(Webserver app, Func<HttpContextBase, Task<AuthContext>> authenticate, IAuthorizationService authz)
        {
            app.Get("/api/v1/usage/snapshots", req => Read(req, authenticate, authz, "snapshots"),
                api => ReadMetadata(api, "snapshots").WithTag("Usage").WithSummary("Read sanitized usage snapshots").WithResponse(200, OpenApiJson.For<EnumerationResult<UsageSnapshotView>>("Snapshots")).WithSecurity("ApiKey"));
            app.Get("/api/v1/usage/latest", req => Read(req, authenticate, authz, "latest"),
                api => ReadMetadata(api, "latest").WithTag("Usage").WithSummary("Read latest account usage").WithResponse(200, OpenApiJson.For<EnumerationResult<UsageSnapshotView>>("Latest")).WithSecurity("ApiKey"));
            app.Get("/api/v1/usage/series", req => Read(req, authenticate, authz, "series"),
                api => ReadMetadata(api, "series").WithTag("Usage").WithSummary("Read bounded usage series").WithResponse(200, OpenApiJson.For<UsageSeriesResult>("Series")).WithSecurity("ApiKey"));
            app.Get("/api/v1/usage/missions", req => Read(req, authenticate, authz, "missions"),
                api => ReadMetadata(api, "missions").WithTag("Usage").WithSummary("Read usage attributed to completed missions").WithResponse(200, OpenApiJson.For<UsageMissionResult>("Missions")).WithSecurity("ApiKey"));
            foreach (string resource in new[] { "snapshots", "latest", "missions" })
            {
                string kind = resource;
                app.Post("/api/v1/usage/" + kind + "/enumerate", req => Read(req, authenticate, authz, kind, true),
                    api => ErrorMetadata(api).WithTag("Usage").WithSummary("Enumerate usage " + kind)
                        .WithRequestBody(OpenApiJson.BodyFor<UsageQuery>("Usage filters and pagination", false))
                        .WithResponse(200, kind == "missions" ? OpenApiJson.For<UsageMissionResult>("Missions") : OpenApiJson.For<EnumerationResult<UsageSnapshotView>>("Usage page")).WithSecurity("ApiKey"));
            }
            app.Post("/api/v1/captains/{id}/usage/capture", async (ApiRequest req) =>
            {
                AuthContext? caller = await Authorize(req, authenticate, authz).ConfigureAwait(false);
                if (caller == null) return Error(req);
                try
                {
                    string id = req.Parameters["id"];
                    // Resolve visibility before reading secondary input.
                    await _Service.ScopeAsync(caller, new UsageQuery { CaptainId = id }).ConfigureAwait(false);
                    UsageCaptureArgs body = String.IsNullOrWhiteSpace(req.Http.Request.DataAsString) ? new UsageCaptureArgs() : JsonSerializer.Deserialize<UsageCaptureArgs>(req.Http.Request.DataAsString, _Options) ?? new UsageCaptureArgs();
                    object receipt = await _Service.CaptureAsync(caller, id, body.HarborId).ConfigureAwait(false);
                    req.Http.Response.StatusCode = receipt is UsageCaptureReceipt ? 202 : 200;
                    return receipt;
                }
                catch (JsonException) { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Invalid JSON body." }; }
                catch (Exception ex) when (RouteErrorMapper.IsMapped(ex)) { return Map(req, ex); }
            }, api => ErrorMetadata(api).WithTag("Usage").WithSummary("Capture captain account usage")
                .WithParameter(OpenApiParameterMetadata.Path("id", "Captain identifier"))
                .WithRequestBody(OpenApiJson.BodyFor<UsageCaptureArgs>("Optional HarborId selects and saves the credential Harbor for idle and scheduled capture", false))
                .WithResponse(200, OpenApiJson.For<EnumerationResult<UsageSnapshotView>>("Original passive observation"))
                .WithResponse(202, OpenApiJson.For<UsageCaptureReceipt>("Capture receipt")).WithSecurity("ApiKey"));
        }

        private static OpenApiRouteMetadata ErrorMetadata(OpenApiRouteMetadata api)
        {
            foreach (int status in new[] { 400, 401, 403, 404, 409, 429 })
                api.WithResponse(status, OpenApiJson.For<ApiErrorResponse>("Usage error; Data.Code identifies capture rejection; 429 includes Retry-After"));
            api.WithResponse(503, OpenApiJson.For<ApiStatusErrorResponse>("Credential host unavailable"));
            return api;
        }

        private static OpenApiRouteMetadata ReadMetadata(OpenApiRouteMetadata api, string kind)
        {
            ErrorMetadata(api);
            foreach (string name in new[] { "captainId", "harborId", "hostId", "runtime", "provider", "accountKey", "tenantId", "from", "to", "pageNumber", "pageSize" })
                api.WithParameter(OpenApiParameterMetadata.Query(name, name == "tenantId" ? "Explicit tenant (global administrator only; omission uses caller tenant)" : name == "pageSize" ? "Default 25, range 1..100" : name == "pageNumber" ? "Default 1" : "Usage filter; UTC ranges are half-open [from,to), at most 90 days", (kind == "series" || kind == "missions") && (name == "from" || name == "to") || kind == "missions" && name == "provider"));
            if (kind == "latest")
                api.WithParameter(OpenApiParameterMetadata.Query("captainIds", "Comma-separated 1..100 distinct tenant-visible captain IDs; incompatible with captainId. Filters before account paging. Shared accounts appear once with full membership. Missing observations return no row, never zero.", false));
            if (kind == "series" || kind == "missions")
                foreach (string name in new[] { "metric", "groupBy", "bucketMinutes", "window", "model", "sortBy", "sortDirection" })
                    api.WithParameter(OpenApiParameterMetadata.Query(name, "Series or mission filter; buckets 5/60/360, metric limits/tokens, groupBy account/provider", false));
            return api;
        }

        private async Task<object> Read(ApiRequest req, Func<HttpContextBase, Task<AuthContext>> authenticate, IAuthorizationService authz, string kind, bool body = false)
        {
            AuthContext? caller = await Authorize(req, authenticate, authz).ConfigureAwait(false);
            if (caller == null) return Error(req);
            try
            {
                if (!body) await _Service.ScopeAsync(caller, new UsageQuery { TenantId = req.Query.GetValueOrDefault("tenantId"), CaptainId = req.Query.GetValueOrDefault("captainId"), CaptainIds = ReadCaptainIds(req), HarborId = req.Query.GetValueOrDefault("harborId") }).ConfigureAwait(false);
                UsageQuery query = body ? (String.IsNullOrWhiteSpace(req.Http.Request.DataAsString) ? new UsageQuery() : JsonSerializer.Deserialize<UsageQuery>(req.Http.Request.DataAsString, _Options) ?? new UsageQuery()) : Parse(req);
                return kind switch {
                    "snapshots" => await _Service.SnapshotsAsync(caller, query).ConfigureAwait(false),
                    "latest" => await _Service.LatestAsync(caller, query).ConfigureAwait(false),
                    "series" => await _Service.SeriesAsync(caller, query).ConfigureAwait(false),
                    _ => await _Service.MissionsAsync(caller, query).ConfigureAwait(false) };
            }
            catch (JsonException) { req.Http.Response.StatusCode = 400; return new ApiErrorResponse { Error = ApiResultEnum.BadRequest, Message = "Invalid JSON body." }; }
            catch (Exception ex) when (RouteErrorMapper.IsMapped(ex)) { return Map(req, ex); }
        }

        private static object Map(ApiRequest req, Exception ex)
        {
            if (ex is UsageRequestException usage)
            {
                req.Http.Response.StatusCode = usage.StatusCode;
                if (usage.StatusCode == 429 && usage.NextEligibleUtc.HasValue)
                    req.Http.Response.Headers.Add("Retry-After", Math.Max(1, (int)Math.Ceiling((usage.NextEligibleUtc.Value - DateTime.UtcNow).TotalSeconds)).ToString(CultureInfo.InvariantCulture));
                if (usage.StatusCode == 503)
                    return new ApiStatusErrorResponse(Armada.Core.Enums.ApiStatusErrorCodeEnum.ServiceUnavailable, usage.Message) { Data = new { Code = usage.Code, usage.NextEligibleUtc } };
                return new ApiErrorResponse { Error = usage.StatusCode == 429 ? ApiResultEnum.SlowDown : RouteErrorMapper.ResultFor(usage.StatusCode), Message = usage.Message, Data = new { Code = usage.Code, usage.NextEligibleUtc } };
            }
            return RouteErrorMapper.ToResponse(req, ex, 409);
        }

        private static List<string>? ReadCaptainIds(ApiRequest req)
        {
            if (!req.Http.Request.QuerystringExists("captainIds")) return null;
            string text = req.Query.GetValueOrDefault("captainIds") ?? String.Empty;
            return new List<string>(Uri.UnescapeDataString(text.Replace("+", " ")).Split(','));
        }

        private static UsageQuery Parse(ApiRequest req)
        {
            UsageQuery result = new UsageQuery();
            foreach (System.Reflection.PropertyInfo property in typeof(UsageQuery).GetProperties())
            {
                string name = Char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1);
                if (property.Name == "CaptainIds") { result.CaptainIds = ReadCaptainIds(req); continue; }
                string? text = req.Query.GetValueOrDefault(name);
                if (property.Name == "FromUtc") text = req.Query.GetValueOrDefault("from") ?? text;
                if (property.Name == "ToUtc") text = req.Query.GetValueOrDefault("to") ?? text;
                if (text == null) continue;
                text = Uri.UnescapeDataString(text.Replace("+", " "));
                Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                try
                {
                    object value = type == typeof(string) ? text : type == typeof(DateTime) ? ParseUtc(text) : type.IsEnum ? Enum.Parse(type, text, true) : Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
                    if (type.IsEnum && !Enum.IsDefined(type, value)) throw new ArgumentException("Unknown enum filter.");
                    property.SetValue(result, value);
                }
                catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is OverflowException) { throw new ArgumentException("Invalid " + name + "."); }
            }
            return result;
        }
        private static DateTime ParseUtc(string text)
        {
            DateTime parsed = DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (parsed.Kind == DateTimeKind.Unspecified || !text.Contains('T')) throw new ArgumentException("UTC ISO-8601 timestamp required.");
            return parsed.ToUniversalTime();
        }

        private static async Task<AuthContext?> Authorize(ApiRequest req, Func<HttpContextBase, Task<AuthContext>> authenticate, IAuthorizationService authz)
        {
            AuthContext caller = await authenticate(req.Http).ConfigureAwait(false);
            if (authz.IsAuthorized(caller, req.Http.Request.Method.ToString(), req.Http.Request.Url.RawWithoutQuery)) return caller;
            req.Http.Response.StatusCode = caller.IsAuthenticated ? 403 : 401; return null;
        }
        private static ApiErrorResponse Error(ApiRequest req) => new ApiErrorResponse { Error = req.Http.Response.StatusCode == 401 ? ApiResultEnum.NotAuthorized : ApiResultEnum.Forbidden, Message = "Usage access denied." };
    }
}
