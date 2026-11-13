namespace Armada.Core.Services
{
    using System;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;

    /// <summary>Provider-neutral historical screenshot data. Writes terminal records without running work or signaling live agents.</summary>
    public static class SyntheticActivityGenerator
    {
        /// <summary>Validate a bounded, fixed-window generation profile before any database changes.</summary>
        /// <param name="request">Profile.</param>
        /// <param name="now">Current UTC time.</param>
        /// <exception cref="ArgumentException">Invalid or excessive profile.</exception>
        public static void Validate(SyntheticActivityRequest request, DateTime now)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!Regex.IsMatch(request.BatchId ?? "", "^[A-Za-z0-9_-]{1,64}$")) throw new ArgumentException("BatchId must be a 1-64 character slug.");
            if (request.Days < 1 || request.Days > 90 || !Double.IsFinite(request.Density) || request.Density < 0.1 || request.Density > 5 || !Double.IsFinite(request.Entropy) || request.Entropy < 0 || request.Entropy > 2) throw new ArgumentException("Days must be 1-90, density 0.1-5, and entropy 0-2.");
            if (request.Days * request.Density * 90 * Math.Exp(request.Entropy) > 50000) throw new ArgumentException("Profile exceeds the 50,000-record planning budget. Lower density, days or entropy.");
            if (request.ToUtc.Kind != DateTimeKind.Utc || request.ToUtc > now || request.ToUtc < now.AddDays(-276)) throw new ArgumentException("ToUtc must be explicit UTC, not future, with the entire window within the past 366 days.");
        }

        /// <summary>Create or resume historical API, mission, event, signal, Harbor, and token activity.</summary>
        /// <param name="database">Initialized storage.</param>
        /// <param name="request">Fixed profile.</param>
        /// <param name="tenantId">Owner tenant.</param>
        /// <param name="userId">Owner user.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Insert counts and synthetic entity identifiers.</returns>
        /// <exception cref="ArgumentException">Invalid profile or ownership.</exception>
        public static async Task<SyntheticActivityResult> GenerateAsync(DatabaseDriver database, SyntheticActivityRequest request, string tenantId, string userId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(database);
            Validate(request, DateTime.UtcNow);
            if (String.IsNullOrWhiteSpace(tenantId) || String.IsNullOrWhiteSpace(userId)) throw new ArgumentException("Tenant and user are required.");
            if (await database.Users.ReadAsync(tenantId, userId, token).ConfigureAwait(false) == null) throw new ArgumentException("User does not exist in the supplied tenant.");
            string identity = tenantId + ":" + userId + ":" + JsonSerializer.Serialize(request);
            string root = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).Substring(0, 20).ToLowerInvariant();
            SyntheticActivityResult result = new SyntheticActivityResult { BatchId = request.BatchId, HarborId = "hbr_syn_" + root, VesselId = "vsl_syn_" + root };
            Random random = new Random(request.Seed);
            DateTime from = request.ToUtc.AddDays(-request.Days);
            string fleetId = "flt_syn_" + root;
            string payload = JsonSerializer.Serialize(new { synthetic = true, batchId = request.BatchId });
            await WriteAsync("fleets", () => database.Fleets.CreateAsync(new Fleet { Id = fleetId, Name = "Screenshot (synthetic) " + root, TenantId = tenantId, UserId = userId }, token), result, token).ConfigureAwait(false);
            await WriteAsync("vessels", () => database.Vessels.CreateAsync(new Vessel { Id = result.VesselId, FleetId = fleetId, RepoUrl = "https://example.invalid/synthetic/" + root + ".git", Name = "Screenshot (synthetic) " + root, TenantId = tenantId, UserId = userId, ProjectContext = "Synthetic screenshot records; no checkout or runtime execution." }, token), result, token).ConfigureAwait(false);
            await WriteAsync("harbors", () => database.Harbors.CreateAsync(new Harbor { Id = result.HarborId, Name = "Screenshot (synthetic) " + root, TenantId = tenantId, UserId = userId, Enabled = false, MaxConcurrentJobs = 4 }, token), result, token).ConfigureAwait(false);
            for (int day = 0; day < request.Days; day++)
            {
                token.ThrowIfCancellationRequested();
                DateTime start = from.AddDays(day);
                double weekend = start.DayOfWeek == DayOfWeek.Saturday || start.DayOfWeek == DayOfWeek.Sunday ? 0.45 : 1;
                double variation = Math.Exp((random.NextDouble() - 0.5) * request.Entropy * 2);
                int requests = Math.Max(1, (int)Math.Round(80 * request.Density * weekend * variation));
                int missions = Math.Max(1, (int)Math.Round(8 * request.Density * weekend * variation));
                if (day == request.Days - 1) { requests = Math.Max(12, requests); missions = Math.Max(12, missions); }
                for (int i = 0; i < requests; i++)
                {
                    DateTime at = day == request.Days - 1 && i < 12 ? request.ToUtc.AddSeconds(-i * 600 - random.Next(1, 600)) : Time(start, request.ToUtc, random);
                    string id = "req_syn_" + root + "_" + day + "_" + i;
                    int code = random.NextDouble() < 0.91 ? 200 : random.NextDouble() < 0.65 ? 400 : 503;
                    string[] routes = new string[] { "/api/v1/status", "/api/v1/missions", "/api/v1/vessels", "/api/v1/token-usage/summary", "/api/v1/captains" };
                    string route = routes[random.Next(routes.Length)];
                    await WriteAsync("requests", () => database.RequestHistory.CreateAsync(new RequestHistoryEntry { Id = id, TenantId = tenantId, UserId = userId, PrincipalDisplay = "synthetic:" + request.BatchId, AuthMethod = "Synthetic", CorrelationId = "synthetic:" + request.BatchId, Method = random.NextDouble() < 0.82 ? "GET" : "POST", Route = route, RouteTemplate = route, StatusCode = code, IsSuccess = code < 400, DurationMs = Math.Round(5 + Math.Pow(random.NextDouble(), 3) * 900, 2), RequestSizeBytes = random.Next(20, 1800), ResponseSizeBytes = random.Next(200, 24000), CreatedUtc = at }, new RequestHistoryDetail { RequestHistoryId = id, RequestBodyText = payload, ResponseBodyText = payload }, token), result, token).ConfigureAwait(false);
                }
                for (int i = 0; i < missions; i++)
                {
                    DateTime end = day == request.Days - 1 && i < 12 ? request.ToUtc.AddSeconds(-i * 600 - random.Next(1, 600)) : Time(start, request.ToUtc, random);
                    DateTime launched = end.AddSeconds(-random.Next(30, 1800));
                    if (launched < from) launched = from;
                    string suffix = root + "_" + day + "_" + i;
                    string missionId = "msn_syn_" + suffix;
                    bool failed = random.NextDouble() < 0.15;
                    DateTime started = launched.AddMilliseconds(Math.Min(2000, (end - launched).TotalMilliseconds / 4));
                    Mission mission = new Mission { Id = missionId, TenantId = tenantId, UserId = userId, VesselId = result.VesselId, AssignedHarborId = result.HarborId, Title = "[Synthetic] " + new string[] { "Review API behavior", "Improve dashboard layout", "Check regression coverage", "Document handoff", "Investigate latency" }[random.Next(5)], Description = "Screenshot simulation. " + payload, FailureKind = failed ? MissionFailureKindEnum.Infra : null, FailureReason = failed ? "Synthetic upstream timeout" : null, Status = failed ? MissionStatusEnum.Failed : MissionStatusEnum.Complete, CreatedUtc = launched, StartedUtc = started, CompletedUtc = end, LastUpdateUtc = end };
                    await WriteAsync("missions", () => database.Missions.CreateAsync(mission, token), result, token).ConfigureAwait(false);
                    await WriteAsync("events", () => database.Events.CreateAsync(new ArmadaEvent { Id = "evt_syn_" + suffix, TenantId = tenantId, UserId = userId, MissionId = missionId, VesselId = result.VesselId, EntityType = "mission", EntityId = missionId, EventType = failed ? "mission.failed" : "mission.completed", Message = mission.Title, Payload = payload, CreatedUtc = end }, token), result, token).ConfigureAwait(false);
                    string[] runtimes = new string[] { "Codex", "ClaudeCode", "Mux" };
                    int runtime = random.Next(3);
                    long firstOutputMs = (long)Math.Min((end - launched).TotalMilliseconds / 2, 2500 + random.Next(100, 25000));
                    await WriteAsync("harborJobs", () => database.HarborJobs.CreateAsync(new HarborJobRecord { Id = "hjb_syn_" + suffix, JobId = "synthetic:" + suffix, HarborId = result.HarborId, TenantId = tenantId, MissionId = missionId, Runtime = runtimes[runtime], Kind = i % 4 == 0 ? HarborJobKindEnum.AskTurn : HarborJobKindEnum.Mission, LaunchedUtc = launched, StartedUtc = started, FirstOutputUtc = launched.AddMilliseconds(firstOutputMs), EndedUtc = end, TimeToFirstOutputMs = firstOutputMs, DurationMs = (long)(end - launched).TotalMilliseconds, ExitCode = failed ? 1 : 0, Outcome = failed ? HarborJobOutcomeEnum.Failed : HarborJobOutcomeEnum.Succeeded }, token), result, token).ConfigureAwait(false);
                    long input = random.Next(500, 30000);
                    long output = random.Next(50, 4000);
                    await WriteAsync("tokens", () => database.TokenUsage.CreateAsync(new TokenUsageRecord { Id = "tku_syn_" + suffix, TenantId = tenantId, UserId = userId, VesselId = result.VesselId, HarborId = result.HarborId, Source = "synthetic", SourceId = "synthetic:" + request.BatchId, Model = new string[] { "gpt-6.1-sol", "claude-sonnet-4", "gpt-oss:120b" }[runtime], Runtime = runtimes[runtime], InputTokens = input, OutputTokens = output, CachedTokens = (long)(input * random.NextDouble() * 0.8), TotalTokens = input + output, Estimated = true, CreatedUtc = end }, token), result, token).ConfigureAwait(false);
                    if (i % 4 == 0) await WriteAsync("signals", () => database.Signals.CreateAsync(new Signal { Id = "sig_syn_" + suffix, TenantId = tenantId, UserId = userId, Read = true, Payload = payload, CreatedUtc = end }, token), result, token).ConfigureAwait(false);
                }
                for (int hour = 0; hour < 24; hour++)
                {
                    DateTime at = start.AddHours(hour);
                    string suffix = root + "_" + day + "_" + hour;
                    int latency = random.Next(15, 180);
                    await WriteAsync("harborSamples", () => database.HarborLinkSamples.CreateAsync(new HarborLinkSample { Id = "hls_syn_" + suffix, HarborId = result.HarborId, BucketStartUtc = at, HeartbeatCount = random.Next(40, 61), RoundTripCount = 10, RoundTripTotalMs = latency * 10, RoundTripMaxMs = latency + random.Next(10, 120), ReconnectCount = random.Next(0, 3), CreatedUtc = at }, token), result, token).ConfigureAwait(false);
                    if (hour % 8 == 0) await WriteAsync("harborEvents", () => database.HarborLinkEvents.CreateAsync(new HarborLinkEvent { Id = "hle_syn_" + suffix, HarborId = result.HarborId, OccurredUtc = at, EventType = hour == 8 && day % 5 == 0 ? HarborLinkEventTypeEnum.Reconnecting : HarborLinkEventTypeEnum.Connected, Detail = payload }, token), result, token).ConfigureAwait(false);
                }
            }
            return result;
        }

        private static DateTime Time(DateTime start, DateTime end, Random random)
        {
            return start.AddSeconds(random.NextDouble() * Math.Min(86400, (end - start).TotalSeconds));
        }

        private static async Task WriteAsync<T>(string kind, Func<Task<T>> write, SyntheticActivityResult result, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try { await write().ConfigureAwait(false); result.Created[kind] = result.Created.GetValueOrDefault(kind) + 1; }
            catch (DuplicateEntityException) { result.Existing++; }
        }
    }
}
