namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for fleet action, run, and run target persistence over a live database: create/read round-trips
    /// of every column, model clamping, tenant isolation, paging and status filters, built-in soft delete versus
    /// user-defined hard delete, unfinished-run enumeration across tenants, run delete removing targets, concurrent
    /// target updates, and targets surviving deletion of their vessel.
    /// </summary>
    public sealed class FleetActionDatabaseSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.FleetAction";
        private static readonly DateTime _Fixed = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Fleet Action database suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("action_create_read_roundtrips_all_fields", "Action CreateAsync/ReadAsync round-trips every column", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                FleetAction action = NewAction(Constants.DefaultTenantId, "Update deps");
                action.UserId = Constants.DefaultUserId;
                action.Description = "bump packages";
                action.Kind = FleetActionKindEnum.Mission;
                action.CommandText = null;
                action.PromptTemplate = "Update {{vessel.name}}";
                action.PipelineId = "ppl_x";
                action.Persona = "Worker";
                action.TimeoutSeconds = 600;
                action.DefaultConcurrency = 6;
                action.RequiresCleanWorkingTree = false;
                action.IsBuiltIn = true;
                action.BuiltInKey = "update-deps";
                action.CreatedUtc = _Fixed;

                FleetAction created = await testDb.Driver.FleetActions.CreateAsync(action).ConfigureAwait(false);
                AssertStartsWith("fac_", created.Id);

                FleetAction? read = await testDb.Driver.FleetActions.ReadAsync(Constants.DefaultTenantId, created.Id).ConfigureAwait(false);
                AssertNotNull(read, "action");
                AssertEqual("Update deps", read!.Name);
                AssertEqual(Constants.DefaultUserId, read.UserId);
                AssertEqual("bump packages", read.Description);
                AssertEqual(FleetActionKindEnum.Mission, read.Kind);
                AssertNull(read.CommandText, "CommandText");
                AssertEqual("Update {{vessel.name}}", read.PromptTemplate);
                AssertEqual("ppl_x", read.PipelineId);
                AssertEqual("Worker", read.Persona);
                AssertEqual(600, read.TimeoutSeconds);
                AssertEqual(6, read.DefaultConcurrency);
                AssertFalse(read.RequiresCleanWorkingTree, "RequiresCleanWorkingTree");
                AssertTrue(read.IsBuiltIn, "IsBuiltIn");
                AssertEqual("update-deps", read.BuiltInKey);
                AssertTrue(read.Active, "Active");
                AssertSameInstant(_Fixed, read.CreatedUtc, "CreatedUtc");
            }));

            cases.Add(CaseAsync("action_update_persists", "Action UpdateAsync persists changes", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                FleetAction created = await testDb.Driver.FleetActions.CreateAsync(NewAction(Constants.DefaultTenantId, "Pull")).ConfigureAwait(false);
                created.Name = "Pull ff-only";
                created.CommandText = "git pull --ff-only";
                created.TimeoutSeconds = 120;
                await testDb.Driver.FleetActions.UpdateAsync(created).ConfigureAwait(false);

                FleetAction? read = await testDb.Driver.FleetActions.ReadAsync(created.Id).ConfigureAwait(false);
                AssertEqual("Pull ff-only", read!.Name);
                AssertEqual("git pull --ff-only", read.CommandText);
                AssertEqual(120, read.TimeoutSeconds);
            }));

            cases.Add(CaseAsync("action_model_clamps", "Action and run settings clamp to their documented ranges", TestTags.Positive, () =>
            {
                FleetAction action = new FleetAction();
                AssertEqual(300, action.TimeoutSeconds, "default timeout");
                AssertEqual(4, action.DefaultConcurrency, "default concurrency");
                AssertTrue(action.RequiresCleanWorkingTree, "default clean tree");
                action.TimeoutSeconds = 1;
                AssertEqual(5, action.TimeoutSeconds);
                action.TimeoutSeconds = 99999;
                AssertEqual(7200, action.TimeoutSeconds);
                action.DefaultConcurrency = 0;
                AssertEqual(1, action.DefaultConcurrency);
                action.DefaultConcurrency = 100;
                AssertEqual(32, action.DefaultConcurrency);

                FleetActionRun run = new FleetActionRun();
                run.Concurrency = 64;
                AssertEqual(32, run.Concurrency);
                run.FailedCount = -3;
                AssertEqual(0, run.FailedCount);

                FleetActionRunTarget target = new FleetActionRunTarget();
                target.DurationMs = -5;
                AssertEqual(0L, target.DurationMs!.Value);
                AssertStartsWith("far_", run.Id);
                AssertStartsWith("fat_", target.Id);
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("action_tenant_isolation", "Another tenant cannot read, enumerate, or delete an action", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                string other = await CreateTenantAsync(db).ConfigureAwait(false);
                FleetAction created = await db.FleetActions.CreateAsync(NewAction(Constants.DefaultTenantId, "Mine")).ConfigureAwait(false);

                AssertNull(await db.FleetActions.ReadAsync(other, created.Id).ConfigureAwait(false), "cross-tenant read");
                EnumerationResult<FleetAction> page = await db.FleetActions.EnumerateAsync(other, new EnumerationQuery()).ConfigureAwait(false);
                AssertEqual(0L, page.TotalRecords, "cross-tenant enumerate");
                await db.FleetActions.DeleteAsync(other, created.Id).ConfigureAwait(false);
                AssertNotNull(await db.FleetActions.ReadAsync(created.Id).ConfigureAwait(false), "cross-tenant delete is a no-op");
            }));

            cases.Add(CaseAsync("builtin_delete_is_soft", "Deleting a built-in action deactivates it and keeps it readable by key", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                FleetAction builtIn = NewAction(Constants.DefaultTenantId, "Fast-forward");
                builtIn.IsBuiltIn = true;
                builtIn.BuiltInKey = "fast-forward";
                builtIn = await db.FleetActions.CreateAsync(builtIn).ConfigureAwait(false);
                FleetAction custom = await db.FleetActions.CreateAsync(NewAction(Constants.DefaultTenantId, "Custom")).ConfigureAwait(false);

                await db.FleetActions.DeleteAsync(Constants.DefaultTenantId, builtIn.Id).ConfigureAwait(false);
                await db.FleetActions.DeleteAsync(custom.Id).ConfigureAwait(false);

                FleetAction? soft = await db.FleetActions.ReadAsync(builtIn.Id).ConfigureAwait(false);
                AssertNotNull(soft, "built-in row kept");
                AssertFalse(soft!.Active, "built-in deactivated");
                FleetAction? byKey = await db.FleetActions.ReadByBuiltInKeyAsync(Constants.DefaultTenantId, "fast-forward").ConfigureAwait(false);
                AssertNotNull(byKey, "ReadByBuiltInKeyAsync includes inactive");
                AssertNull(await db.FleetActions.ReadByBuiltInKeyAsync(Constants.DefaultTenantId, "missing").ConfigureAwait(false), "unknown key");
                AssertNull(await db.FleetActions.ReadAsync(custom.Id).ConfigureAwait(false), "user-defined action hard deleted");

                EnumerationResult<FleetAction> active = await db.FleetActions.EnumerateAsync(Constants.DefaultTenantId, new EnumerationQuery()).ConfigureAwait(false);
                AssertEqual(0L, active.TotalRecords, "inactive excluded by default");
                EnumerationResult<FleetAction> all = await db.FleetActions.EnumerateAsync(Constants.DefaultTenantId, new EnumerationQuery(), true).ConfigureAwait(false);
                AssertEqual(1L, all.TotalRecords, "includeInactive");
            }));

            cases.Add(CaseAsync("action_enumerate_pages", "Action EnumerateAsync pages and orders by creation time", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                for (int i = 0; i < 5; i++)
                {
                    FleetAction action = NewAction(Constants.DefaultTenantId, "A" + i);
                    action.CreatedUtc = _Fixed.AddMinutes(i);
                    await testDb.Driver.FleetActions.CreateAsync(action).ConfigureAwait(false);
                }

                EnumerationQuery query = new EnumerationQuery();
                query.PageSize = 2;
                query.Order = EnumerationOrderEnum.CreatedAscending;
                EnumerationResult<FleetAction> first = await testDb.Driver.FleetActions.EnumerateAsync(Constants.DefaultTenantId, query).ConfigureAwait(false);
                AssertEqual(5L, first.TotalRecords);
                AssertEqual(3, first.TotalPages);
                AssertEqual("A0", first.Objects[0].Name);
                AssertEqual("A1", first.Objects[1].Name);
            }));

            cases.Add(CaseAsync("run_create_read_roundtrips_all_fields", "Run CreateAsync/ReadAsync round-trips every column", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                FleetActionRun run = NewRun(Constants.DefaultTenantId);
                run.UserId = Constants.DefaultUserId;
                run.ActionId = "fac_x";
                run.ActionName = "Build";
                run.Kind = FleetActionKindEnum.Command;
                run.CommandText = "dotnet build";
                run.TimeoutSeconds = 900;
                run.RequiresCleanWorkingTree = false;
                run.Concurrency = 3;
                run.Status = FleetActionRunStatusEnum.CompletedWithFailures;
                run.TargetCount = 10;
                run.SucceededCount = 6;
                run.FailedCount = 2;
                run.SkippedCount = 1;
                run.CancelledCount = 1;
                run.StartedUtc = _Fixed;
                run.CompletedUtc = _Fixed.AddMinutes(5);

                FleetActionRun created = await testDb.Driver.FleetActionRuns.CreateAsync(run).ConfigureAwait(false);
                FleetActionRun? read = await testDb.Driver.FleetActionRuns.ReadAsync(Constants.DefaultTenantId, created.Id).ConfigureAwait(false);
                AssertNotNull(read, "run");
                AssertEqual("fac_x", read!.ActionId);
                AssertEqual("Build", read.ActionName);
                AssertEqual("dotnet build", read.CommandText);
                AssertEqual(900, read.TimeoutSeconds);
                AssertFalse(read.RequiresCleanWorkingTree, "RequiresCleanWorkingTree");
                AssertEqual(3, read.Concurrency);
                AssertEqual(FleetActionRunStatusEnum.CompletedWithFailures, read.Status);
                AssertEqual(10, read.TargetCount);
                AssertEqual(6, read.SucceededCount);
                AssertEqual(2, read.FailedCount);
                AssertEqual(1, read.SkippedCount);
                AssertEqual(1, read.CancelledCount);
                AssertSameInstant(_Fixed, read.StartedUtc!.Value, "StartedUtc");
                AssertSameInstant(_Fixed.AddMinutes(5), read.CompletedUtc!.Value, "CompletedUtc");
            }));

            cases.Add(CaseAsync("run_enumerate_status_and_unfinished", "Run EnumerateAsync filters by status; EnumerateUnfinishedAsync spans tenants", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                string other = await CreateTenantAsync(db).ConfigureAwait(false);

                FleetActionRun pending = NewRun(Constants.DefaultTenantId);
                await db.FleetActionRuns.CreateAsync(pending).ConfigureAwait(false);
                FleetActionRun running = NewRun(Constants.DefaultTenantId);
                running.Status = FleetActionRunStatusEnum.Running;
                await db.FleetActionRuns.CreateAsync(running).ConfigureAwait(false);
                FleetActionRun done = NewRun(Constants.DefaultTenantId);
                done.Status = FleetActionRunStatusEnum.Completed;
                await db.FleetActionRuns.CreateAsync(done).ConfigureAwait(false);
                FleetActionRun otherRunning = NewRun(other);
                otherRunning.Status = FleetActionRunStatusEnum.Running;
                await db.FleetActionRuns.CreateAsync(otherRunning).ConfigureAwait(false);

                EnumerationQuery query = new EnumerationQuery();
                query.Status = "Running";
                EnumerationResult<FleetActionRun> runningPage = await db.FleetActionRuns.EnumerateAsync(Constants.DefaultTenantId, query).ConfigureAwait(false);
                AssertEqual(1L, runningPage.TotalRecords, "status filter is tenant-scoped");
                AssertEqual(running.Id, runningPage.Objects[0].Id);

                EnumerationResult<FleetActionRun> allMine = await db.FleetActionRuns.EnumerateAsync(Constants.DefaultTenantId, new EnumerationQuery()).ConfigureAwait(false);
                AssertEqual(3L, allMine.TotalRecords);

                List<FleetActionRun> unfinished = await db.FleetActionRuns.EnumerateUnfinishedAsync().ConfigureAwait(false);
                AssertEqual(3, unfinished.Count, "pending + running across tenants");
                AssertTrue(unfinished.Any(r => r.Id == otherRunning.Id), "other tenant included");
                AssertFalse(unfinished.Any(r => r.Id == done.Id), "completed excluded");
                AssertNull(await db.FleetActionRuns.ReadAsync(other, running.Id).ConfigureAwait(false), "cross-tenant run read");
            }));

            cases.Add(CaseAsync("run_delete_removes_targets", "Run DeleteAsync removes its targets", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                FleetActionRun run = await db.FleetActionRuns.CreateAsync(NewRun(Constants.DefaultTenantId)).ConfigureAwait(false);
                FleetActionRunTarget target = await db.FleetActionRunTargets.CreateAsync(NewTarget(run, "vsl_a", "alpha")).ConfigureAwait(false);

                await db.FleetActionRuns.DeleteAsync(Constants.DefaultTenantId, run.Id).ConfigureAwait(false);
                AssertNull(await db.FleetActionRuns.ReadAsync(run.Id).ConfigureAwait(false), "run deleted");
                AssertNull(await db.FleetActionRunTargets.ReadAsync(target.Id).ConfigureAwait(false), "target deleted with run");
            }));

            cases.Add(CaseAsync("target_create_read_roundtrips_all_fields", "Target CreateAsync/ReadAsync round-trips every column including unmanaged output", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                FleetActionRun run = await db.FleetActionRuns.CreateAsync(NewRun(Constants.DefaultTenantId)).ConfigureAwait(false);
                FleetActionRunTarget target = NewTarget(run, "vsl_a", "alpha");
                target.Status = FleetActionTargetStatusEnum.TimedOut;
                target.SkipReason = "DirtyTree";
                target.FailureReason = "Interrupted";
                target.RenderedText = "git pull --ff-only";
                target.ExitCode = 137;
                target.OutputText = new string('o', 5000);
                target.ErrorText = "fatal: boom";
                target.OutputTruncated = true;
                target.VoyageId = "vyg_x";
                target.StartedUtc = _Fixed;
                target.CompletedUtc = _Fixed.AddSeconds(30);
                target.DurationMs = 30000;
                await db.FleetActionRunTargets.CreateAsync(target).ConfigureAwait(false);

                FleetActionRunTarget? read = await db.FleetActionRunTargets.ReadAsync(Constants.DefaultTenantId, target.Id).ConfigureAwait(false);
                AssertNotNull(read, "target");
                AssertEqual(run.Id, read!.RunId);
                AssertEqual("vsl_a", read.VesselId);
                AssertEqual("alpha", read.VesselName);
                AssertEqual(FleetActionTargetStatusEnum.TimedOut, read.Status);
                AssertEqual("DirtyTree", read.SkipReason);
                AssertEqual("Interrupted", read.FailureReason);
                AssertEqual("git pull --ff-only", read.RenderedText);
                AssertEqual(137, read.ExitCode!.Value);
                AssertEqual(5000, read.OutputText!.Length);
                AssertEqual("fatal: boom", read.ErrorText);
                AssertTrue(read.OutputTruncated, "OutputTruncated");
                AssertEqual("vyg_x", read.VoyageId);
                AssertEqual(30000L, read.DurationMs!.Value);
                AssertSameInstant(_Fixed.AddSeconds(30), read.CompletedUtc!.Value, "CompletedUtc");
            }));

            cases.Add(CaseAsync("target_enumerate_by_run_pages_and_filters", "Target EnumerateByRunAsync pages, filters by status, and is tenant-scoped", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                string other = await CreateTenantAsync(db).ConfigureAwait(false);
                FleetActionRun run = await db.FleetActionRuns.CreateAsync(NewRun(Constants.DefaultTenantId)).ConfigureAwait(false);
                for (int i = 0; i < 5; i++)
                {
                    FleetActionRunTarget target = NewTarget(run, "vsl_" + i, "vessel-" + i);
                    target.Status = i % 2 == 0 ? FleetActionTargetStatusEnum.Succeeded : FleetActionTargetStatusEnum.Failed;
                    await db.FleetActionRunTargets.CreateAsync(target).ConfigureAwait(false);
                }

                EnumerationResult<FleetActionRunTarget> page2 = await db.FleetActionRunTargets.EnumerateByRunAsync(Constants.DefaultTenantId, run.Id, null, 2, 2).ConfigureAwait(false);
                AssertEqual(5L, page2.TotalRecords);
                AssertEqual(3, page2.TotalPages);
                AssertEqual(2, page2.Objects.Count);
                AssertEqual("vessel-2", page2.Objects[0].VesselName, "ordered by vessel name");

                EnumerationResult<FleetActionRunTarget> failed = await db.FleetActionRunTargets.EnumerateByRunAsync(Constants.DefaultTenantId, run.Id, FleetActionTargetStatusEnum.Failed, 1, 50).ConfigureAwait(false);
                AssertEqual(2L, failed.TotalRecords, "status filter");
                AssertTrue(failed.Objects.All(t => t.Status == FleetActionTargetStatusEnum.Failed), "only failed");

                EnumerationResult<FleetActionRunTarget> cross = await db.FleetActionRunTargets.EnumerateByRunAsync(other, run.Id, null, 1, 50).ConfigureAwait(false);
                AssertEqual(0L, cross.TotalRecords, "cross-tenant enumerate");

                List<FleetActionRunTarget> all = await db.FleetActionRunTargets.ReadAllByRunAsync(run.Id).ConfigureAwait(false);
                AssertEqual(5, all.Count, "ReadAllByRunAsync");

                await db.FleetActionRunTargets.DeleteByRunAsync(run.Id).ConfigureAwait(false);
                AssertEqual(0, (await db.FleetActionRunTargets.ReadAllByRunAsync(run.Id).ConfigureAwait(false)).Count, "DeleteByRunAsync");
                AssertNotNull(await db.FleetActionRuns.ReadAsync(run.Id).ConfigureAwait(false), "DeleteByRunAsync keeps the run");
            }));

            cases.Add(CaseAsync("concurrent_target_updates_all_persist", "Concurrent target updates all persist", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                FleetActionRun run = await db.FleetActionRuns.CreateAsync(NewRun(Constants.DefaultTenantId)).ConfigureAwait(false);
                List<FleetActionRunTarget> targets = new List<FleetActionRunTarget>();
                for (int i = 0; i < 16; i++)
                    targets.Add(await db.FleetActionRunTargets.CreateAsync(NewTarget(run, "vsl_c" + i, "c-" + i.ToString("D2"))).ConfigureAwait(false));

                List<Task> updates = new List<Task>();
                foreach (FleetActionRunTarget target in targets)
                {
                    FleetActionRunTarget captured = target;
                    updates.Add(Task.Run(async () =>
                    {
                        captured.Status = FleetActionTargetStatusEnum.Succeeded;
                        captured.ExitCode = 0;
                        captured.OutputText = "ok " + captured.VesselName;
                        await db.FleetActionRunTargets.UpdateAsync(captured).ConfigureAwait(false);
                    }));
                }

                await Task.WhenAll(updates).ConfigureAwait(false);

                List<FleetActionRunTarget> all = await db.FleetActionRunTargets.ReadAllByRunAsync(run.Id).ConfigureAwait(false);
                AssertEqual(16, all.Count);
                AssertTrue(all.All(t => t.Status == FleetActionTargetStatusEnum.Succeeded && t.OutputText == "ok " + t.VesselName), "every concurrent update persisted");
            }));

            cases.Add(CaseAsync("targets_survive_vessel_delete", "Targets keep their rows when the vessel is deleted", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Vessel vessel = new Vessel("action-" + Guid.NewGuid().ToString("N").Substring(0, 8), "https://example.com/r.git");
                vessel.TenantId = Constants.DefaultTenantId;
                vessel = await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                FleetActionRun run = await db.FleetActionRuns.CreateAsync(NewRun(Constants.DefaultTenantId)).ConfigureAwait(false);
                FleetActionRunTarget target = await db.FleetActionRunTargets.CreateAsync(NewTarget(run, vessel.Id, vessel.Name)).ConfigureAwait(false);

                await db.Vessels.DeleteAsync(vessel.Id).ConfigureAwait(false);
                FleetActionRunTarget? read = await db.FleetActionRunTargets.ReadAsync(target.Id).ConfigureAwait(false);
                AssertNotNull(read, "target survives");
                AssertEqual(vessel.Name, read!.VesselName);
            }));

            cases.Add(CaseAsync("null_arguments_throw", "Null entities and identifiers throw ArgumentNullException", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentNullException>(() => testDb.Driver.FleetActions.CreateAsync(null!));
                await AssertThrowsAsync<ArgumentNullException>(() => testDb.Driver.FleetActions.ReadByBuiltInKeyAsync(Constants.DefaultTenantId, null!));
                await AssertThrowsAsync<ArgumentNullException>(() => testDb.Driver.FleetActionRuns.ReadAsync(null!));
                await AssertThrowsAsync<ArgumentNullException>(() => testDb.Driver.FleetActionRunTargets.EnumerateByRunAsync(Constants.DefaultTenantId, null!, null, 1, 10));
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Fleet Action Database",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static FleetAction NewAction(string tenantId, string name)
        {
            FleetAction action = new FleetAction();
            action.TenantId = tenantId;
            action.Name = name;
            action.Kind = FleetActionKindEnum.Command;
            action.CommandText = "git status";
            return action;
        }

        private static FleetActionRun NewRun(string tenantId)
        {
            FleetActionRun run = new FleetActionRun();
            run.TenantId = tenantId;
            run.ActionName = "Ad hoc";
            run.CommandText = "git status";
            return run;
        }

        private static FleetActionRunTarget NewTarget(FleetActionRun run, string vesselId, string vesselName)
        {
            FleetActionRunTarget target = new FleetActionRunTarget();
            target.TenantId = run.TenantId;
            target.RunId = run.Id;
            target.VesselId = vesselId;
            target.VesselName = vesselName;
            return target;
        }

        private static async Task<string> CreateTenantAsync(DatabaseDriver db)
        {
            TenantMetadata tenant = new TenantMetadata("Action Tenant " + Guid.NewGuid().ToString("N").Substring(0, 6));
            await db.Tenants.CreateAsync(tenant).ConfigureAwait(false);
            return tenant.Id;
        }

        private static void AssertSameInstant(DateTime expected, DateTime actual, string label)
        {
            double delta = Math.Abs((expected.ToUniversalTime() - actual.ToUniversalTime()).TotalMilliseconds);
            AssertTrue(delta < 1.0, label + ": expected " + expected.ToString("o") + " but was " + actual.ToString("o"));
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
