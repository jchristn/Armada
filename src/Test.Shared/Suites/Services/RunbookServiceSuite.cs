namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Runbook operations apply only to playbook rows whose content starts with the runbook metadata marker.
    /// </summary>
    public sealed class RunbookServiceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.RunbookService";
        private const string MetadataMarker = "<!-- armada:runbook";
        private const string NotFoundMessage = "Runbook not found.";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("list_omits_plain_playbook", "Runbook list includes a marked runbook and omits a plain playbook", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver database = testDb.Driver;
                    RunbookService service = CreateService(database);
                    AuthContext auth = Operator();
                    Runbook marked = await service.CreateAsync(auth, new RunbookUpsertRequest
                    {
                        FileName = "release-checklist.md",
                        Title = "Release checklist",
                        OverviewMarkdown = "Confirm the build.\n"
                    }).ConfigureAwait(false);
                    Playbook plain = await StorePlaybookAsync(database, "plain-guide.md", "# Plain playbook\n\nKeep this text.\n").ConfigureAwait(false);

                    EnumerationResult<Runbook> page = await service.EnumerateAsync(auth, ListQuery()).ConfigureAwait(false);

                    Assert(ListsId(page, marked.Id), "marked runbook stays in the list");
                    Assert(!ListsId(page, plain.Id), "plain playbook stays out of the runbook list");
                }
            }));

            cases.Add(CaseAsync("read_plain_playbook_returns_null", "Reading a plain playbook through the runbook API returns null", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver database = testDb.Driver;
                    RunbookService service = CreateService(database);
                    Playbook plain = await StorePlaybookAsync(database, "plain-guide.md", "# Plain playbook\n\nKeep this text.\n").ConfigureAwait(false);

                    Runbook? read = await service.ReadAsync(Operator(), plain.Id).ConfigureAwait(false);

                    AssertNull(read, "plain playbook is not a runbook");
                }
            }));

            cases.Add(CaseAsync("update_plain_playbook_is_not_found", "Updating a plain playbook reports not found and keeps its content", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver database = testDb.Driver;
                    RunbookService service = CreateService(database);
                    AuthContext auth = Operator();
                    string content = "# Plain playbook\n\nKeep this text.\n";
                    Playbook plain = await StorePlaybookAsync(database, "plain-guide.md", content).ConfigureAwait(false);

                    await ExpectRunbookNotFoundAsync(() => service.UpdateAsync(auth, plain.Id, new RunbookUpsertRequest
                    {
                        FileName = "plain-guide.md",
                        Title = "Replaced title",
                        OverviewMarkdown = "Replaced overview\n"
                    })).ConfigureAwait(false);

                    Playbook? stored = await ReadStoredAsync(database, plain.Id).ConfigureAwait(false);
                    AssertNotNull(stored, "plain playbook row remains");
                    AssertEqual(content, stored!.Content, "plain playbook content remains");
                }
            }));

            cases.Add(CaseAsync("delete_plain_playbook_is_not_found", "Deleting a plain playbook reports not found and keeps the row", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver database = testDb.Driver;
                    RunbookService service = CreateService(database);
                    string content = "# Plain playbook\n\nKeep this text.\n";
                    Playbook plain = await StorePlaybookAsync(database, "plain-guide.md", content).ConfigureAwait(false);

                    await ExpectRunbookNotFoundAsync(() => service.DeleteAsync(Operator(), plain.Id)).ConfigureAwait(false);

                    Playbook? stored = await ReadStoredAsync(database, plain.Id).ConfigureAwait(false);
                    AssertNotNull(stored, "plain playbook row remains");
                    AssertEqual(content, stored!.Content, "plain playbook content remains");
                }
            }));

            cases.Add(CaseAsync("start_execution_plain_playbook_is_not_found", "Starting execution for a plain playbook reports not found", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver database = testDb.Driver;
                    RunbookService service = CreateService(database);
                    AuthContext auth = Operator();
                    string content = "# Plain playbook\n\nKeep this text.\n";
                    Playbook plain = await StorePlaybookAsync(database, "plain-guide.md", content).ConfigureAwait(false);

                    await ExpectRunbookNotFoundAsync(() => service.StartExecutionAsync(auth, plain.Id, new RunbookExecutionStartRequest
                    {
                        Title = "Drill"
                    })).ConfigureAwait(false);

                    EnumerationResult<RunbookExecution> executions = await service.EnumerateExecutionsAsync(auth, new RunbookExecutionQuery
                    {
                        RunbookId = plain.Id,
                        PageNumber = 1,
                        PageSize = 100
                    }).ConfigureAwait(false);
                    AssertEqual(0, executions.Objects.Count, "no execution is stored");
                    Playbook? stored = await ReadStoredAsync(database, plain.Id).ConfigureAwait(false);
                    AssertNotNull(stored, "plain playbook row remains");
                    AssertEqual(content, stored!.Content, "plain playbook content remains");
                }
            }));

            cases.Add(CaseAsync("leading_whitespace_is_omitted", "A marker after leading whitespace is not a runbook", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver database = testDb.Driver;
                    RunbookService service = CreateService(database);
                    AuthContext auth = Operator();
                    string content = " " + MetadataMarker + "\n-->\n\n# Padded guidance\n";
                    Playbook padded = await StorePlaybookAsync(database, "padded-guide.md", content).ConfigureAwait(false);

                    Runbook? read = await service.ReadAsync(auth, padded.Id).ConfigureAwait(false);
                    AssertNull(read, "leading whitespace keeps the row out of runbook reads");

                    EnumerationResult<Runbook> page = await service.EnumerateAsync(auth, ListQuery()).ConfigureAwait(false);
                    Assert(!ListsId(page, padded.Id), "leading whitespace keeps the row out of the runbook list");
                }
            }));

            cases.Add(CaseAsync("leading_whitespace_update_and_delete_are_not_found", "Update, delete, and start-execution leave a padded marker row intact", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver database = testDb.Driver;
                    RunbookService service = CreateService(database);
                    AuthContext auth = Operator();
                    string content = " " + MetadataMarker + "\n-->\n\n# Padded guidance\n";
                    Playbook padded = await StorePlaybookAsync(database, "padded-guide.md", content).ConfigureAwait(false);

                    await ExpectRunbookNotFoundAsync(() => service.UpdateAsync(auth, padded.Id, new RunbookUpsertRequest
                    {
                        FileName = "padded-guide.md",
                        Title = "Replaced title",
                        OverviewMarkdown = "Replaced overview\n"
                    })).ConfigureAwait(false);
                    Playbook? afterUpdate = await ReadStoredAsync(database, padded.Id).ConfigureAwait(false);
                    AssertNotNull(afterUpdate, "padded row remains after update");
                    AssertEqual(content, afterUpdate!.Content, "padded content remains after update");

                    await ExpectRunbookNotFoundAsync(() => service.DeleteAsync(auth, padded.Id)).ConfigureAwait(false);
                    Playbook? afterDelete = await ReadStoredAsync(database, padded.Id).ConfigureAwait(false);
                    AssertNotNull(afterDelete, "padded row remains after delete");
                    AssertEqual(content, afterDelete!.Content, "padded content remains after delete");

                    await ExpectRunbookNotFoundAsync(() => service.StartExecutionAsync(auth, padded.Id, new RunbookExecutionStartRequest
                    {
                        Title = "Drill"
                    })).ConfigureAwait(false);
                    EnumerationResult<RunbookExecution> executions = await service.EnumerateExecutionsAsync(auth, new RunbookExecutionQuery
                    {
                        RunbookId = padded.Id,
                        PageNumber = 1,
                        PageSize = 100
                    }).ConfigureAwait(false);
                    AssertEqual(0, executions.Objects.Count, "no execution is stored");
                }
            }));

            cases.Add(CaseAsync("marked_runbook_round_trip", "A marked runbook can be created, read, updated, started, and deleted", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver database = testDb.Driver;
                    RunbookService service = CreateService(database);
                    AuthContext auth = Operator();
                    Runbook created = await service.CreateAsync(auth, new RunbookUpsertRequest
                    {
                        FileName = "release-checklist.md",
                        Title = "Release checklist",
                        Description = "Operator steps",
                        OverviewMarkdown = "Confirm the build.\n",
                        Steps = new List<RunbookStep>
                        {
                            new RunbookStep { Title = "Confirm", Instructions = "Read the log." }
                        }
                    }).ConfigureAwait(false);

                    Runbook? read = await service.ReadAsync(auth, created.Id).ConfigureAwait(false);
                    AssertNotNull(read, "created runbook can be read");
                    AssertEqual("Release checklist", read!.Title, "created title");
                    AssertEqual("Confirm the build.\n", read.OverviewMarkdown, "created overview");
                    AssertEqual(1, read.Steps.Count, "created step");
                    AssertEqual("Confirm", read.Steps[0].Title, "created step title");
                    Assert(ListsId(await service.EnumerateAsync(auth, ListQuery()).ConfigureAwait(false), created.Id), "created runbook is listed");

                    Runbook updated = await service.UpdateAsync(auth, created.Id, new RunbookUpsertRequest
                    {
                        FileName = "release-checklist.md",
                        Title = "Release checklist revised",
                        Description = "Revised steps",
                        OverviewMarkdown = "Confirm the revision.\n",
                        Steps = new List<RunbookStep>
                        {
                            new RunbookStep { Title = "Confirm", Instructions = "Read the log." }
                        }
                    }).ConfigureAwait(false);
                    AssertEqual("Release checklist revised", updated.Title, "updated title");
                    Runbook? reread = await service.ReadAsync(auth, created.Id).ConfigureAwait(false);
                    AssertNotNull(reread, "updated runbook can be read");
                    AssertEqual("Release checklist revised", reread!.Title, "reread title");
                    AssertEqual("Revised steps", reread.Description, "reread description");
                    AssertEqual("Confirm the revision.\n", reread.OverviewMarkdown, "reread overview");
                    Playbook? stored = await ReadStoredAsync(database, created.Id).ConfigureAwait(false);
                    AssertNotNull(stored, "backing playbook remains");
                    Assert(stored!.Content.StartsWith(MetadataMarker, StringComparison.Ordinal), "stored content starts with the runbook marker");

                    RunbookExecution execution = await service.StartExecutionAsync(auth, created.Id, new RunbookExecutionStartRequest
                    {
                        Title = "Release drill"
                    }).ConfigureAwait(false);
                    AssertEqual(created.Id, execution.RunbookId, "execution uses the runbook");
                    AssertEqual(RunbookExecutionStatusEnum.Running, execution.Status, "execution starts running");

                    await service.DeleteAsync(auth, created.Id).ConfigureAwait(false);
                    AssertNull(await service.ReadAsync(auth, created.Id).ConfigureAwait(false), "deleted runbook is not readable");
                    AssertNull(await ReadStoredAsync(database, created.Id).ConfigureAwait(false), "backing playbook is deleted");
                    Assert(!ListsId(await service.EnumerateAsync(auth, ListQuery()).ConfigureAwait(false), created.Id), "deleted runbook leaves the list");
                }
            }));

            cases.Add(CaseAsync("marker_prefix_remains_a_runbook", "Content that starts with the marker remains a runbook when the comment is unclosed", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver database = testDb.Driver;
                    RunbookService service = CreateService(database);
                    AuthContext auth = Operator();
                    Playbook prefixed = await StorePlaybookAsync(database, "open-marker.md", MetadataMarker).ConfigureAwait(false);

                    Runbook? read = await service.ReadAsync(auth, prefixed.Id).ConfigureAwait(false);
                    AssertNotNull(read, "a leading marker is a runbook");
                    AssertEqual(prefixed.Id, read!.Id, "read returns the stored row");
                    Assert(ListsId(await service.EnumerateAsync(auth, ListQuery()).ConfigureAwait(false), prefixed.Id), "a leading marker is listed");

                    Runbook updated = await service.UpdateAsync(auth, prefixed.Id, new RunbookUpsertRequest
                    {
                        FileName = "open-marker.md",
                        Title = "Open marker",
                        OverviewMarkdown = "Still a runbook.\n"
                    }).ConfigureAwait(false);
                    AssertEqual("Open marker", updated.Title, "prefix row can be updated");
                    Playbook? stored = await ReadStoredAsync(database, prefixed.Id).ConfigureAwait(false);
                    AssertNotNull(stored, "prefix row remains");
                    Assert(stored!.Content.StartsWith(MetadataMarker, StringComparison.Ordinal), "updated content starts with the runbook marker");
                }
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Runbook Service", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        private static RunbookService CreateService(DatabaseDriver database)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return new RunbookService(database, logging);
        }

        private static AuthContext Operator()
        {
            return AuthContext.Authenticated(
                Constants.DefaultTenantId,
                Constants.DefaultUserId,
                false,
                true,
                "Test");
        }

        private static RunbookQuery ListQuery()
        {
            return new RunbookQuery
            {
                PageNumber = 1,
                PageSize = 100
            };
        }

        private static async Task<Playbook> StorePlaybookAsync(DatabaseDriver database, string fileName, string content)
        {
            Playbook playbook = new Playbook(fileName, content)
            {
                TenantId = Constants.DefaultTenantId,
                UserId = Constants.DefaultUserId,
                Scope = ScopeEnum.TenantWide,
                Description = "Stored guidance"
            };
            return await database.Playbooks.CreateAsync(playbook).ConfigureAwait(false);
        }

        private static async Task<Playbook?> ReadStoredAsync(DatabaseDriver database, string id)
        {
            return await database.Playbooks.ReadAsync(Constants.DefaultTenantId, id).ConfigureAwait(false);
        }

        private static bool ListsId(EnumerationResult<Runbook> page, string id)
        {
            foreach (Runbook item in page.Objects)
            {
                if (String.Equals(item.Id, id, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static async Task ExpectRunbookNotFoundAsync(Func<Task> action)
        {
            bool thrown = false;
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (KeyNotFoundException ex)
            {
                thrown = true;
                AssertEqual(NotFoundMessage, ex.Message, "not found message");
            }

            Assert(thrown, "expected KeyNotFoundException but no exception was thrown");
        }

        #endregion
    }
}
