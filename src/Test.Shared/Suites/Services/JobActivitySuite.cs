namespace Test.Shared.Suites.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Background work feeds the existing audit and attention surfaces without routine inbox noise.</summary>
    public sealed class JobActivitySuite : IArmadaTestSuite
    {
        public TestSuiteDescriptor Build()
        {
            const string suite = "Services.JobActivity";
            return new(suite, "Background work activity", new List<TestCaseDescriptor>
            {
                new(suite, "terminal_outcomes", "Only winning terminal transitions record scoped events; failures signal without copying opaque results", async ct =>
                {
                    using TestDatabase f = await TestDatabaseHelper.CreateDatabaseAsync(); await Owners(f);
                    LoggingModule log = Log(); JobService service = new(f.Driver, log);
                    foreach (JobStatusEnum status in new[] { JobStatusEnum.Succeeded, JobStatusEnum.Failed, JobStatusEnum.Cancelled })
                    {
                        Job job = await service.EnqueueAsync("test " + status, JobKindEnum.Generic, "ten_a", "usr_a", ct);
                        AssertTrue(await service.TryFinishAsync(job.Id, status, "opaque-private-result", status == JobStatusEnum.Failed ? "Disk full" : null, ct));
                        AssertFalse(await service.TryFinishAsync(job.Id, JobStatusEnum.Failed, null, "late failure", ct));
                        List<ArmadaEvent> events = await f.Driver.Events.EnumerateByEntityAsync("job", job.Id, token: ct);
                        AssertEqual(1, events.Count); AssertEqual("job." + status.ToString().ToLowerInvariant(), events[0].EventType);
                        AssertEqual("ten_a", events[0].TenantId); AssertEqual("usr_a", events[0].UserId);
                        AssertFalse(events[0].Payload!.Contains("opaque-private-result"));
                    }
                    var signals = await f.Driver.Signals.EnumerateAsync(new EnumerationQuery(), ct);
                    AssertEqual(1L, signals.TotalRecords); AssertEqual(SignalTypeEnum.Error, signals.Objects[0].Type);
                    AssertEqual("ten_a", signals.Objects[0].TenantId); AssertEqual("usr_a", signals.Objects[0].UserId);
                    AssertContains("Disk full", signals.Objects[0].Payload!);
                }),
                new(suite, "usage_probe_noise", "Successful probes and discarded captures stay out of Needs You; actual capture failures are actionable", async ct =>
                {
                    using TestDatabase f = await TestDatabaseHelper.CreateDatabaseAsync(); await Owners(f); JobService jobs = new(f.Driver, Log());
                    foreach (string reason in new[] { "SettingsGenerationChanged", "CaptureDisabled", "NoMembers" })
                    {
                        Job job = await jobs.EnqueueAsync("discard " + reason, JobKindEnum.UsageProbe, "ten_a", null, ct);
                        await jobs.TryFinishAsync(job.Id, JobStatusEnum.Failed, null, reason, ct);
                    }
                    Job ok = await jobs.EnqueueAsync("healthy", JobKindEnum.UsageProbe, "ten_a", null, ct);
                    await jobs.TryFinishAsync(ok.Id, JobStatusEnum.Succeeded, null, null, ct);
                    InboxService inbox = new(f.Driver, Log()); AuthContext admin = AuthContext.Authenticated("ten_a", "admin", false, true, "Test");
                    AssertEqual(0L, (await inbox.EnumerateAsync(admin, new EnumerationQuery(), ct)).TotalRecords);
                    AssertEqual(0L, (await f.Driver.Signals.EnumerateAsync(new EnumerationQuery(), ct)).TotalRecords);
                    Job failed = await jobs.EnqueueAsync(UsageAccountResolver.JobName(UsageAccountResolver.GroupKey("ten_a", "hbr_fixture", AgentRuntimeEnum.ClaudeCode, null)), JobKindEnum.UsageProbe, "ten_a", null, ct);
                    await jobs.TryFinishAsync(failed.Id, JobStatusEnum.Failed, null, "HarborUnavailable", ct);
                    var result = await inbox.EnumerateAsync(admin, new EnumerationQuery(), ct);
                    AssertEqual(1L, result.TotalRecords); AssertEqual(InboxItemKinds.BackgroundJobFailed, result.Objects[0].Kind);
                    AssertEqual("/activity?source=usage-overview&hostId=hbr_fixture", result.Objects[0].Href);
                    AssertContains("ClaudeCode", result.Objects[0].Title);
                }),
                new(suite, "retry_supersedes_failure", "Needs You keeps exact scope and paging, and removes a failure when its own operation is retried", async ct =>
                {
                    using TestDatabase f = await TestDatabaseHelper.CreateDatabaseAsync(); await Owners(f); JobService jobs = new(f.Driver, Log());
                    Job mine = await jobs.EnqueueAsync("sync", JobKindEnum.Sync, "ten_a", "usr_a", ct);
                    await jobs.TryFinishAsync(mine.Id, JobStatusEnum.Failed, null, "network down", ct);
                    foreach (var owner in new[] { ("ten_a", "usr_b"), ("ten_b", "usr_c") })
                    {
                        Job other = await jobs.EnqueueAsync("sync", JobKindEnum.Sync, owner.Item1, owner.Item2, ct);
                        await jobs.TryFinishAsync(other.Id, JobStatusEnum.Succeeded, null, null, ct);
                    }
                    InboxService inbox = new(f.Driver, Log()); AuthContext user = AuthContext.Authenticated("ten_a", "usr_a", false, false, "Test");
                    var page = await inbox.EnumerateAsync(user, new EnumerationQuery { PageSize = 1 }, ct);
                    AssertEqual(1L, page.TotalRecords); AssertEqual(mine.Id, page.Objects[0].EntityId);
                    AssertEqual(0, (await inbox.EnumerateAsync(user, new EnumerationQuery { PageSize = 1, PageNumber = 2 }, ct)).Objects.Count);
                    AssertEqual(1L, await inbox.CountAsync(user, new[] { InboxItemKinds.BackgroundJobFailed }, ct));
                    Job retry = await jobs.EnqueueAsync("sync", JobKindEnum.Sync, "ten_a", "usr_a", ct);
                    AssertEqual(0L, (await inbox.EnumerateAsync(user, new EnumerationQuery(), ct)).TotalRecords);
                    await jobs.TryFinishAsync(retry.Id, JobStatusEnum.Failed, null, "still down", ct);
                    page = await inbox.EnumerateAsync(user, new EnumerationQuery(), ct);
                    AssertEqual(1L, page.TotalRecords); AssertEqual(retry.Id, page.Objects[0].EntityId);
                    AssertEqual(0L, (await inbox.EnumerateAsync(AuthContext.Authenticated("ten_b", "usr_b", false, false, "Test"), new EnumerationQuery(), ct)).TotalRecords);
                }),
                new(suite, "stale_and_cancel", "Stale workers emit one failure outcome; operator cancellations emit audit only", async ct =>
                {
                    using TestDatabase f = await TestDatabaseHelper.CreateDatabaseAsync(); await Owners(f); JobService jobs = new(f.Driver, Log());
                    Job stale = await jobs.EnqueueAsync("stale", JobKindEnum.Generic, "ten_a", "usr_a", ct);
                    stale.Status = JobStatusEnum.Running; stale.LastUpdateUtc = DateTime.UtcNow.AddHours(-2);
                    await f.Driver.Jobs.UpdateAsync(stale, ct);
                    await jobs.MaintainAsync(ct); await jobs.MaintainAsync(ct);
                    AssertEqual(1, (await f.Driver.Events.EnumerateByEntityAsync("job", stale.Id, token: ct)).Count);
                    Job cancelled = await jobs.EnqueueAsync("cancel", JobKindEnum.Generic, "ten_a", "usr_a", ct);
                    await jobs.CancelAsync(cancelled, ct);
                    AssertEqual("job.cancelled", (await f.Driver.Events.EnumerateByEntityAsync("job", cancelled.Id, token: ct)).Single().EventType);
                    AssertEqual(1L, (await f.Driver.Signals.EnumerateAsync(new EnumerationQuery(), ct)).TotalRecords);
                })
            });
        }

        private static async Task Owners(TestDatabase f)
        {
            foreach (string tenant in new[] { "ten_a", "ten_b" })
                await f.Driver.Tenants.CreateAsync(new TenantMetadata(tenant) { Id = tenant });
            foreach (var owner in new[] { ("ten_a", "usr_a"), ("ten_a", "usr_b"), ("ten_b", "usr_c") })
                await f.Driver.Users.CreateAsync(new UserMaster(owner.Item1, owner.Item2 + "@example.invalid", "fixture-password") { Id = owner.Item2 });
        }

        private static LoggingModule Log() { LoggingModule log = new(); log.Settings.EnableConsole = false; return log; }
    }
}
