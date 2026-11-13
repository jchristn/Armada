namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Metrics;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Server;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Collection bounds on the captain, Harbor, runtime, model endpoint, and persona write and read paths
    /// (docs/ENUMERATION.md): every configuration list stored on an entity is rejected (never truncated) one item over
    /// its limit by the shared service layer that REST, MCP, WebSocket, and the Harbor link all use; server-owned lists
    /// cannot be set by a caller; and computed lists (Harbor metrics, Mux endpoint pages) are bounded by explicit
    /// aggregation or paging without dropping data.
    /// </summary>
    public sealed class ConfigCollectionBoundsSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.ConfigCollectionBounds";
        private static readonly DateTime _Now = new DateTime(2026, 10, 8, 14, 37, 20, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("harbor_capabilities_bounded_on_create", "A Harbor create with 257 capabilities is rejected naming Capabilities and 256; 256 is accepted", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, Logging());
                AuthContext admin = new AuthContext { IsAuthenticated = true, IsAdmin = true, TenantId = "ten_b", UserId = "usr_b" };
                CollectionLimitExceededException ex = await ExpectLimitAsync(() => harbors.CreateAsync(admin, HarborWith(257, "over"))).ConfigureAwait(false);
                AssertEqual("Capabilities", ex.Field, "field");
                AssertEqual(256, ex.Limit, "limit");
                AssertEqual(257L, ex.Actual, "actual");
                AssertEqual(0L, (await harbors.EnumeratePageAsync(admin, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "nothing stored");
                Harbor ok = await harbors.CreateAsync(admin, HarborWith(256, "at-limit")).ConfigureAwait(false);
                AssertEqual(256, (await testDb.Driver.Harbors.ReadAsync(ok.Id).ConfigureAwait(false))!.Capabilities.Count, "256 stored");
            }));

            cases.Add(CaseAsync("harbor_handshake_capabilities_refused", "A Harbor link handshake advertising 257 capabilities is refused with the reason and registers nothing", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                HarborService harbors = new HarborService(testDb.Driver, Logging());
                HarborConnectionManager manager = new HarborConnectionManager(harbors, Logging(), null);
                HarborHandshake handshake = new HarborHandshake { HarborId = "hbr_caps", Name = "Caps", ProtocolVersion = "1.0" };
                for (int i = 0; i < 257; i++) handshake.Capabilities.Add(new HarborCapability { Name = "tool-" + i, Available = true });
                HarborSendDelegate silent = (message, cancellation) => Task.CompletedTask;
                HarborHandshakeAck ack = await manager.OnHandshakeAsync(handshake, "ten_caps", "usr_caps", silent).ConfigureAwait(false);
                AssertFalse(ack.Accepted, "refused");
                AssertTrue((ack.Reason ?? "").Contains("Capabilities", StringComparison.Ordinal), "the reason names the field: " + ack.Reason);
                AssertNull(await testDb.Driver.Harbors.ReadAsync("hbr_caps").ConfigureAwait(false), "not registered");
                AssertFalse(manager.IsConnected("hbr_caps"), "no live link");
            }));

            cases.Add(CaseAsync("model_endpoint_lists_bounded_and_history_server_owned", "An endpoint create stores no caller health history and rejects 257 headers; an update that would store 257 headers is rejected and leaves the endpoint unchanged", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(testDb.Driver, Logging());
                AuthContext admin = new AuthContext { IsAuthenticated = true, IsAdmin = true, TenantId = "ten_e", UserId = "usr_e" };

                ModelEndpoint withHistory = EndpointFor("history");
                for (int i = 0; i < 600; i++) withHistory.HealthHistory.Add(new ModelEndpointHealthRecord { TimestampUtc = _Now.AddMinutes(-i), Success = true });
                ModelEndpoint created = await service.CreateAsync(admin, withHistory).ConfigureAwait(false);
                ModelEndpoint? stored = await testDb.Driver.ModelEndpoints.ReadAsync(created.Id).ConfigureAwait(false);
                AssertEqual(0, stored!.HealthHistory.Count, "caller-supplied history ignored");

                ModelEndpoint tooMany = EndpointFor("headers");
                for (int i = 0; i < 257; i++) tooMany.Headers.Add(new ModelEndpointHeader("X-Extra-" + i, "v"));
                CollectionLimitExceededException ex = await ExpectLimitAsync(() => service.CreateAsync(admin, tooMany)).ConfigureAwait(false);
                AssertEqual("Headers", ex.Field, "field");

                ModelEndpoint update = EndpointFor("history");
                update.Id = created.Id;
                List<ModelEndpointHeader> headers = new List<ModelEndpointHeader>();
                for (int i = 0; i < 257; i++) headers.Add(new ModelEndpointHeader("X-Extra-" + i, "v"));
                update.Headers = headers;
                await ExpectLimitAsync(() => service.UpdateAsync(admin, update)).ConfigureAwait(false);
                AssertEqual(0, (await testDb.Driver.ModelEndpoints.ReadAsync(created.Id).ConfigureAwait(false))!.Headers.Count, "update not stored");
            }));

            cases.Add(CaseAsync("persona_priorities_bounded", "A persona create or update with 257 captain priorities is rejected (CaptainPriorities, 256); 256 is accepted", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                PersonaWriteService writes = new PersonaWriteService(testDb.Driver);
                AuthContext admin = new AuthContext { IsAuthenticated = true, IsAdmin = true, TenantId = Armada.Core.Constants.DefaultTenantId, UserId = Armada.Core.Constants.DefaultUserId };
                Persona over = new Persona { Name = "bounded-over", PromptTemplateName = "persona.worker", CaptainPriorities = Priorities(257) };
                CollectionLimitExceededException ex = await ExpectLimitAsync(() => writes.CreateAsync(admin, over)).ConfigureAwait(false);
                AssertEqual("CaptainPriorities", ex.Field, "field");
                AssertEqual(256, ex.Limit, "limit");

                PersonaWriteResult ok = await writes.CreateAsync(admin, new Persona { Name = "bounded-ok", PromptTemplateName = "persona.worker", CaptainPriorities = Priorities(256) }).ConfigureAwait(false);
                AssertEqual(PersonaWriteStatusEnum.Ok, ok.Status, "256 accepted: " + ok.Message);
                await ExpectLimitAsync(() => writes.UpdateAsync(admin, "bounded-ok", new PersonaUpdateRequest { CaptainPriorities = Priorities(257) }, false)).ConfigureAwait(false);
                AssertEqual(256, (await testDb.Driver.Personas.ReadByNameAsync("bounded-ok").ConfigureAwait(false))!.CaptainPriorities.Count, "update not stored");
            }));

            cases.Add(Case("mux_endpoint_page", "A Mux endpoint list is ordered by name, paged with exact totals, empty past the end, and rejects an endpoint with 257 headers", () =>
            {
                MuxEndpointListResult list = new MuxEndpointListResult { Success = true, ContractVersion = 1, ConfigDirectory = "/cfg" };
                foreach (string name in new[] { "zeta", "Alpha", "beta", "gamma", "delta" }) list.Endpoints.Add(new MuxEndpointInfo { Name = name });
                MuxEndpointPage first = MuxCliService.ToPage(list, new EnumerationQuery { PageSize = 2 });
                AssertEqual(5L, first.TotalRecords, "total");
                AssertEqual(3, first.TotalPages, "pages");
                AssertTrue(first.Objects.Select(e => e.Name).SequenceEqual(new[] { "Alpha", "beta" }), "ordered by name, case-insensitive");
                AssertEqual("/cfg", first.ConfigDirectory, "metadata carried");
                MuxEndpointPage last = MuxCliService.ToPage(list, new EnumerationQuery { PageNumber = 3, PageSize = 2 });
                AssertEqual("zeta", last.Objects.Single().Name, "final page");
                MuxEndpointPage past = MuxCliService.ToPage(list, new EnumerationQuery { PageNumber = 9, PageSize = 2 });
                AssertEqual(0, past.Objects.Count, "past the end");
                AssertEqual(5L, past.TotalRecords, "total kept");

                MuxEndpointInfo wide = new MuxEndpointInfo { Name = "wide" };
                for (int i = 0; i < 257; i++) wide.HeaderNames.Add("X-" + i);
                list.Endpoints.Add(wide);
                bool rejected = false;
                try { MuxCliService.ToPage(list, new EnumerationQuery()); }
                catch (CollectionLimitExceededException ex) { rejected = ex.Field.EndsWith("HeaderNames", StringComparison.Ordinal); }
                AssertTrue(rejected, "an endpoint over the header bound is rejected, not truncated");
            }));

            cases.Add(Case("harbor_metrics_launch_speed_aggregates_runtimes", "Twenty runtimes list the 15 busiest and one Other entry that counts every remaining job", () =>
            {
                HarborMetricsInput input = MetricsInput();
                for (int r = 0; r < 20; r++)
                    for (int j = 0; j <= r; j++)
                        input.Jobs.Add(new HarborJobRecord { JobId = "j_" + r + "_" + j, HarborId = "hbr_m", Runtime = "rt" + r.ToString("00"), LaunchedUtc = _Now.AddMinutes(-30), EndedUtc = _Now.AddMinutes(-20), Outcome = HarborJobOutcomeEnum.Succeeded, TimeToFirstOutputMs = 100, DurationMs = 1000 });
                HarborMetrics metrics = HarborMetricsBuilder.Build(input);
                AssertEqual(HarborMetricsRanges.MaxLaunchSpeedRuntimes, metrics.LaunchSpeed.Count, "bounded");
                HarborLaunchSpeed other = metrics.LaunchSpeed.Last();
                AssertTrue(other.IsOther, "last is the aggregate");
                AssertEqual(5, other.RuntimeCount, "five runtimes aggregated");
                AssertEqual(1 + 2 + 3 + 4 + 5, other.JobCount, "every job of the aggregated runtimes counted");
                AssertEqual(input.Jobs.Count, metrics.LaunchSpeed.Sum(s => s.JobCount), "no job dropped");
                AssertEqual("rt19", metrics.LaunchSpeed[0].Runtime, "busiest first");
                CollectionBounds.Validate(metrics);
            }));

            cases.Add(Case("harbor_metrics_token_series_aggregate", "Twenty-five runtime and model pairs list the top 19 and one other entry, overall and per bucket, with the token totals preserved", () =>
            {
                HarborMetricsInput input = MetricsInput();
                for (int i = 0; i < 25; i++)
                    input.TokenRecords.Add(new TokenUsageRecord { Runtime = "Codex", Model = "model-" + i.ToString("00"), CreatedUtc = _Now.AddMinutes(-10), InputTokens = 10 * (i + 1), TotalTokens = 10 * (i + 1) });
                HarborMetrics metrics = HarborMetricsBuilder.Build(input);
                AssertEqual(HarborMetricsRanges.MaxTokenSeries, metrics.Tokens.Series.Count, "overall bounded");
                HarborTokenSeries other = metrics.Tokens.Series.Last();
                AssertTrue(other.IsOther, "aggregate last");
                AssertEqual(6, other.SeriesCount, "six series aggregated");
                AssertEqual(metrics.Tokens.TotalTokens, metrics.Tokens.Series.Sum(s => s.TotalTokens), "overall total preserved");
                foreach (HarborTokenBucket bucket in metrics.Tokens.Buckets)
                {
                    AssertTrue(bucket.Series.Count <= HarborMetricsRanges.MaxTokenSeries, "bucket bounded");
                    AssertEqual(bucket.TotalTokens, bucket.Series.Sum(s => s.TotalTokens), "bucket total preserved");
                }

                CollectionBounds.Validate(metrics);
            }));

            cases.Add(Case("harbor_metrics_link_segments_coalesce", "More than 1000 link transitions are shown at bucket resolution with SegmentsCoalesced, while ConnectedPercent uses every stretch", () =>
            {
                HarborMetricsInput input = MetricsInput();
                DateTime start = _Now.AddMinutes(-59);
                for (int i = 0; i < 1600; i++)
                    input.Events.Add(new HarborLinkEvent { HarborId = "hbr_m", EventType = i % 2 == 0 ? HarborLinkEventTypeEnum.Connected : HarborLinkEventTypeEnum.Reconnecting, OccurredUtc = start.AddSeconds(i * 2) });
                HarborMetrics metrics = HarborMetricsBuilder.Build(input);
                AssertTrue(metrics.Link.SegmentsCoalesced, "coalesced");
                AssertTrue(metrics.Link.RawSegmentCount > HarborMetricsRanges.MaxLinkSegments, "raw count reported: " + metrics.Link.RawSegmentCount);
                AssertTrue(metrics.Link.Segments.Count <= HarborMetricsRanges.MaxBucketCount, "at most one segment per bucket: " + metrics.Link.Segments.Count);
                AssertNotNull(metrics.Link.ConnectedPercent, "connected percent still computed");
                for (int i = 1; i < metrics.Link.Segments.Count; i++)
                    AssertEqual(metrics.Link.Segments[i - 1].EndUtc, metrics.Link.Segments[i].StartUtc, "contiguous");

                HarborMetricsInput quiet = MetricsInput();
                quiet.Events.Add(new HarborLinkEvent { HarborId = "hbr_m", EventType = HarborLinkEventTypeEnum.Connected, OccurredUtc = start });
                HarborMetrics calm = HarborMetricsBuilder.Build(quiet);
                AssertFalse(calm.Link.SegmentsCoalesced, "a calm window is not coalesced");
                AssertEqual(calm.Link.RawSegmentCount, calm.Link.Segments.Count, "every stretch shown");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Collection bounds for captains, Harbors, runtimes, model endpoints, and personas",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule Logging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static Harbor HarborWith(int capabilities, string name)
        {
            Harbor harbor = new Harbor { Name = name };
            for (int i = 0; i < capabilities; i++) harbor.Capabilities.Add(new HarborCapability { Name = "tool-" + i, Available = true });
            return harbor;
        }

        private static ModelEndpoint EndpointFor(string name)
        {
            return new ModelEndpoint
            {
                Name = name,
                Kind = ModelEndpointKindEnum.Inference,
                Provider = ModelProviderEnum.Ollama,
                BaseUrl = "http://localhost:11434",
                Model = "qwen3"
            };
        }

        private static List<PersonaCaptainPriority> Priorities(int count)
        {
            List<PersonaCaptainPriority> list = new List<PersonaCaptainPriority>();
            for (int i = 0; i < count; i++) list.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex, Model = "model-" + i });
            return list;
        }

        private static HarborMetricsInput MetricsInput()
        {
            return new HarborMetricsInput
            {
                Harbor = new Harbor { Id = "hbr_m", Name = "Metrics", MaxConcurrentJobs = 4 },
                Range = HarborMetricsRangeEnum.OneHour,
                NowUtc = _Now
            };
        }

        private static async Task<CollectionLimitExceededException> ExpectLimitAsync(Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (CollectionLimitExceededException ex)
            {
                return ex;
            }

            throw new InvalidOperationException("Expected CollectionLimitExceededException.");
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { TestTags.Positive });
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.Positive });
        }

        #endregion
    }
}
