namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Server;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Model profiles (migration 107, docs/MODEL_HETEROGENEITY.md Phase 5 item 1) on the configured provider: profile and
    /// revision round trips (fully and minimally populated), the unique identity key, updates that write only the mutable
    /// parts, filtered and paged enumeration, symmetric compatibility marks that never edit a revision, the captain
    /// reference (captains.model_profile_id), and the find-or-create rules: a runtime patch or minor release pools into
    /// the current revision while a new major version, model digest, served context or sampling starts a new one, and the
    /// same identity twice (or concurrently) gives one profile.
    /// </summary>
    public sealed class ModelProfileSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.ModelProfiles";
        private static readonly DateTime _Fixed = new DateTime(2026, 10, 26, 1, 2, 3, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("profile_roundtrip", "A fully and a minimally populated profile round-trip every persisted field", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                ModelProfile full = new ModelProfile
                {
                    TenantId = "ten_full",
                    Identity = new ModelProfileIdentity { Runtime = "Mux", Provider = "Ollama", Model = "qwen3.8:27b", ServedContextTokens = 131072 },
                    Capabilities = new ModelProfileCapabilities { ToolsAvailable = true, McpAvailable = false },
                    Rendering = new ModelProfileRendering { Template = "Compact", RepeatContractAtEnd = true, MaxFilesPerMission = 3 },
                    Notes = "Slow but careful.",
                    CreatedUtc = _Fixed
                };
                await db.ModelProfiles.CreateAsync(full, "key-full").ConfigureAwait(false);
                ModelProfile? read = await db.ModelProfiles.ReadAsync(full.Id).ConfigureAwait(false);
                AssertNotNull(read, "read back");
                AssertTrue(read!.Id.StartsWith("mdl_", StringComparison.Ordinal), "mdl_ prefix: " + read.Id);
                AssertEqual("ten_full", read.TenantId);
                AssertEqual("Mux", read.Identity.Runtime);
                AssertEqual("Ollama", read.Identity.Provider);
                AssertEqual("qwen3.8:27b", read.Identity.Model);
                AssertEqual((int?)131072, read.Identity.ServedContextTokens);
                AssertEqual((bool?)true, read.Capabilities.ToolsAvailable);
                AssertEqual((bool?)false, read.Capabilities.McpAvailable);
                AssertEqual("Compact", read.Rendering.Template);
                AssertEqual((bool?)true, read.Rendering.RepeatContractAtEnd);
                AssertEqual((int?)3, read.Rendering.MaxFilesPerMission);
                AssertEqual("Slow but careful.", read.Notes);
                AssertSameInstant(_Fixed, read.CreatedUtc, "created");
                AssertEqual(0, read.Revisions.Count, "ReadAsync returns no revisions");
                AssertEqual(full.Id, (await db.ModelProfiles.ReadByIdentityKeyAsync("key-full").ConfigureAwait(false))!.Id, "by identity key");

                ModelProfile minimal = new ModelProfile { Identity = new ModelProfileIdentity { Runtime = "ClaudeCode" } };
                await db.ModelProfiles.CreateAsync(minimal, "key-min").ConfigureAwait(false);
                ModelProfile? min = await db.ModelProfiles.ReadAsync(minimal.Id).ConfigureAwait(false);
                AssertNotNull(min, "minimal read back");
                AssertNull(min!.TenantId, "tenant");
                AssertEqual("ClaudeCode", min.Identity.Runtime);
                AssertNull(min.Identity.Provider, "provider");
                AssertNull(min.Identity.Model, "model");
                AssertNull(min.Identity.ServedContextTokens, "served context");
                AssertNull(min.Capabilities.ToolsAvailable, "tools");
                AssertNull(min.Capabilities.McpAvailable, "mcp");
                AssertNull(min.Rendering.Template, "template");
                AssertNull(min.Rendering.RepeatContractAtEnd, "repeat");
                AssertNull(min.Rendering.MaxFilesPerMission, "max files");
                AssertNull(min.Notes, "notes");
                AssertNull(await db.ModelProfiles.ReadAsync("mdl_missing").ConfigureAwait(false), "missing");
                AssertNull(await db.ModelProfiles.ReadByIdentityKeyAsync("key-missing").ConfigureAwait(false), "missing key");
            }));

            cases.Add(CaseAsync("identity_key_unique", "A second profile with the same identity key is a DuplicateEntityException", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                await db.ModelProfiles.CreateAsync(new ModelProfile { Identity = new ModelProfileIdentity { Runtime = "Codex" } }, "key-dup").ConfigureAwait(false);
                await AssertThrowsAsync<DuplicateEntityException>(() => db.ModelProfiles.CreateAsync(new ModelProfile { Identity = new ModelProfileIdentity { Runtime = "Codex" } }, "key-dup"), "duplicate").ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("update_mutable_only", "UpdateAsync writes capabilities, rendering and notes but never the identity, tenant or creation time", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfile profile = new ModelProfile { TenantId = "ten_u", Identity = new ModelProfileIdentity { Runtime = "Mux", Model = "m1", ServedContextTokens = 8192 }, CreatedUtc = _Fixed };
                await db.ModelProfiles.CreateAsync(profile, "key-u").ConfigureAwait(false);

                profile.TenantId = "ten_other";
                profile.CreatedUtc = _Fixed.AddDays(3);
                profile.Identity = new ModelProfileIdentity { Runtime = "Codex", Model = "m2", ServedContextTokens = 4096 };
                profile.Capabilities = new ModelProfileCapabilities { ToolsAvailable = false, McpAvailable = true };
                profile.Rendering = new ModelProfileRendering { Template = "Full", RepeatContractAtEnd = false, MaxFilesPerMission = 1 };
                profile.Notes = "edited";
                await db.ModelProfiles.UpdateAsync(profile).ConfigureAwait(false);

                ModelProfile read = (await db.ModelProfiles.ReadAsync(profile.Id).ConfigureAwait(false))!;
                AssertEqual("ten_u", read.TenantId, "tenant unchanged");
                AssertSameInstant(_Fixed, read.CreatedUtc, "created unchanged");
                AssertEqual("Mux", read.Identity.Runtime, "runtime unchanged");
                AssertEqual("m1", read.Identity.Model, "model unchanged");
                AssertEqual((int?)8192, read.Identity.ServedContextTokens, "served context unchanged");
                AssertEqual((bool?)false, read.Capabilities.ToolsAvailable);
                AssertEqual((bool?)true, read.Capabilities.McpAvailable);
                AssertEqual("Full", read.Rendering.Template);
                AssertEqual((bool?)false, read.Rendering.RepeatContractAtEnd);
                AssertEqual((int?)1, read.Rendering.MaxFilesPerMission);
                AssertEqual("edited", read.Notes);

                profile.Capabilities = new ModelProfileCapabilities();
                profile.Rendering = new ModelProfileRendering();
                profile.Notes = null;
                await db.ModelProfiles.UpdateAsync(profile).ConfigureAwait(false);
                ModelProfile cleared = (await db.ModelProfiles.ReadAsync(profile.Id).ConfigureAwait(false))!;
                AssertNull(cleared.Capabilities.ToolsAvailable, "tools cleared");
                AssertNull(cleared.Rendering.Template, "template cleared");
                AssertNull(cleared.Rendering.MaxFilesPerMission, "max files cleared");
                AssertNull(cleared.Notes, "notes cleared");
            }));

            cases.Add(CaseAsync("enumerate_filters_paging", "EnumerateAsync filters by tenant, runtime, provider and model text and pages newest first", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfile a = new ModelProfile { TenantId = "ten_a", Identity = new ModelProfileIdentity { Runtime = "Mux", Provider = "Ollama", Model = "qwen3.8:27b" }, CreatedUtc = _Fixed };
                ModelProfile b = new ModelProfile { TenantId = "ten_a", Identity = new ModelProfileIdentity { Runtime = "ClaudeCode", Model = "opus" }, CreatedUtc = _Fixed.AddMinutes(1) };
                ModelProfile c = new ModelProfile { TenantId = "ten_a", Identity = new ModelProfileIdentity { Runtime = "Mux", Provider = "OpenAI", Model = "deepseek_v4%" }, CreatedUtc = _Fixed.AddMinutes(2) };
                ModelProfile d = new ModelProfile { TenantId = "ten_b", Identity = new ModelProfileIdentity { Runtime = "Mux", Provider = "Ollama", Model = "qwen3.8:27b" }, CreatedUtc = _Fixed.AddMinutes(3) };
                await db.ModelProfiles.CreateAsync(a, "e-a").ConfigureAwait(false);
                await db.ModelProfiles.CreateAsync(b, "e-b").ConfigureAwait(false);
                await db.ModelProfiles.CreateAsync(c, "e-c").ConfigureAwait(false);
                await db.ModelProfiles.CreateAsync(d, "e-d").ConfigureAwait(false);

                EnumerationResult<ModelProfile> all = await db.ModelProfiles.EnumerateAsync(new ModelProfileQuery()).ConfigureAwait(false);
                AssertEqual(4L, all.TotalRecords, "all");
                AssertEqual(d.Id, all.Objects[0].Id, "newest first");
                EnumerationResult<ModelProfile> tenantA = await db.ModelProfiles.EnumerateAsync(new ModelProfileQuery { TenantId = "ten_a", PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(3L, tenantA.TotalRecords, "tenant a");
                AssertEqual(2, tenantA.TotalPages, "two pages");
                AssertEqual(2, tenantA.Objects.Count, "page of two");
                AssertEqual(c.Id, tenantA.Objects[0].Id, "page one newest");
                EnumerationResult<ModelProfile> page2 = await db.ModelProfiles.EnumerateAsync(new ModelProfileQuery { TenantId = "ten_a", PageSize = 2, PageNumber = 2 }).ConfigureAwait(false);
                AssertEqual(a.Id, page2.Objects.Single().Id, "page two");
                EnumerationResult<ModelProfile> mux = await db.ModelProfiles.EnumerateAsync(new ModelProfileQuery { TenantId = "ten_a", Runtime = "mux" }).ConfigureAwait(false);
                AssertEqual(2L, mux.TotalRecords, "runtime case-insensitive");
                EnumerationResult<ModelProfile> ollama = await db.ModelProfiles.EnumerateAsync(new ModelProfileQuery { Provider = "OLLAMA" }).ConfigureAwait(false);
                AssertEqual(2L, ollama.TotalRecords, "provider across tenants");
                EnumerationResult<ModelProfile> qwen = await db.ModelProfiles.EnumerateAsync(new ModelProfileQuery { TenantId = "ten_a", ModelContains = "QWEN" }).ConfigureAwait(false);
                AssertEqual(a.Id, qwen.Objects.Single().Id, "model contains");
                EnumerationResult<ModelProfile> wildcard = await db.ModelProfiles.EnumerateAsync(new ModelProfileQuery { ModelContains = "_v4%" }).ConfigureAwait(false);
                AssertEqual(c.Id, wildcard.Objects.Single().Id, "LIKE wildcards are literal");
                EnumerationResult<ModelProfile> none = await db.ModelProfiles.EnumerateAsync(new ModelProfileQuery { TenantId = "ten_none" }).ConfigureAwait(false);
                AssertEqual(0L, none.TotalRecords, "empty");
                AssertEqual(0, none.Objects.Count, "no objects");
            }));

            cases.Add(CaseAsync("revision_roundtrip_and_order", "Revisions round-trip every field, list oldest first, and the interface offers no way to edit or delete one", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfile profile = new ModelProfile { TenantId = "ten_r", Identity = new ModelProfileIdentity { Runtime = "Mux" } };
                await db.ModelProfiles.CreateAsync(profile, "key-r").ConfigureAwait(false);

                ModelProfileRevision second = new ModelProfileRevision
                {
                    ProfileId = profile.Id, TenantId = "ten_r", RuntimeVersion = "2.0.0", ModelDigest = "sha256:bbb",
                    Sampling = new ModelProfileSampling { Temperature = 0.1, TopP = 0.95 },
                    ToolConfiguration = new ModelProfileToolConfiguration { Tools = true, Mcp = false },
                    FirstSeenUtc = _Fixed.AddHours(1)
                };
                ModelProfileRevision first = new ModelProfileRevision { ProfileId = profile.Id, TenantId = "ten_r", FirstSeenUtc = _Fixed };
                await db.ModelProfiles.CreateRevisionAsync(second).ConfigureAwait(false);
                await db.ModelProfiles.CreateRevisionAsync(first).ConfigureAwait(false);

                ModelProfileRevision read = (await db.ModelProfiles.ReadRevisionAsync(second.Id).ConfigureAwait(false))!;
                AssertTrue(read.Id.StartsWith("mdv_", StringComparison.Ordinal), "mdv_ prefix: " + read.Id);
                AssertEqual(profile.Id, read.ProfileId);
                AssertEqual("ten_r", read.TenantId);
                AssertEqual("2.0.0", read.RuntimeVersion);
                AssertEqual("sha256:bbb", read.ModelDigest);
                AssertEqual((double?)0.1, read.Sampling.Temperature);
                AssertEqual((double?)0.95, read.Sampling.TopP);
                AssertEqual((bool?)true, read.ToolConfiguration.Tools);
                AssertEqual((bool?)false, read.ToolConfiguration.Mcp);
                AssertSameInstant(_Fixed.AddHours(1), read.FirstSeenUtc, "first seen");
                AssertEqual(0, read.CompatibleWith.Count, "no marks");

                ModelProfileRevision minimal = (await db.ModelProfiles.ReadRevisionAsync(first.Id).ConfigureAwait(false))!;
                AssertNull(minimal.RuntimeVersion, "version");
                AssertNull(minimal.ModelDigest, "digest");
                AssertNull(minimal.Sampling.Temperature, "temperature");
                AssertNull(minimal.Sampling.TopP, "top-p");
                AssertNull(minimal.ToolConfiguration.Tools, "tools");
                AssertNull(minimal.ToolConfiguration.Mcp, "mcp");

                List<ModelProfileRevision> list = await db.ModelProfiles.ReadRevisionsAsync(profile.Id).ConfigureAwait(false);
                AssertEqual(2, list.Count, "two revisions");
                AssertEqual(first.Id, list[0].Id, "oldest first");
                AssertEqual(second.Id, list[1].Id, "newest last");
                AssertEqual(0, (await db.ModelProfiles.ReadRevisionsAsync("mdl_none").ConfigureAwait(false)).Count, "no revisions");
                AssertNull(await db.ModelProfiles.ReadRevisionAsync("mdv_missing").ConfigureAwait(false), "missing revision");

                string[] mutators = typeof(IModelProfileMethods).GetMethods()
                    .Where(m => m.Name.Contains("Revision", StringComparison.Ordinal) && (m.Name.StartsWith("Update", StringComparison.Ordinal) || m.Name.StartsWith("Delete", StringComparison.Ordinal)))
                    .Select(m => m.Name).ToArray();
                AssertEqual(0, mutators.Length, "no revision update or delete: " + String.Join(", ", mutators));
            }));

            cases.Add(CaseAsync("revision_evaluation_report_roundtrip", "An advisory revision's evaluation run id and report (migration 109) round-trip; an observed revision keeps them null", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfile profile = new ModelProfile { Identity = new ModelProfileIdentity { Runtime = "Mux" } };
                await db.ModelProfiles.CreateAsync(profile, "key-eval").ConfigureAwait(false);
                ModelProfileRevision observed = await db.ModelProfiles.CreateRevisionAsync(new ModelProfileRevision { ProfileId = profile.Id, RuntimeVersion = "1.0.0", FirstSeenUtc = _Fixed }).ConfigureAwait(false);
                ModelEvaluationReport report = new ModelEvaluationReport { RunId = "mpr_eval", Complete = true, ProfileId = profile.Id };
                report.Narrative = new ModelEvaluationNarrative { Status = ModelEvaluationNarrativeStatusEnum.Accepted, HowToWork = "One task at a time." };
                report.Narrative.Strengths.Add(new ModelEvaluationFinding { Key = "persona:Judge", Text = "Strong: reviews (3 of 3).", Passed = 3, Total = 3 });
                ModelProfileRevision advisory = await db.ModelProfiles.CreateRevisionAsync(new ModelProfileRevision
                {
                    ProfileId = profile.Id,
                    RuntimeVersion = "1.0.0",
                    FirstSeenUtc = _Fixed.AddMinutes(1),
                    EvaluationRunId = "mpr_eval",
                    EvaluationReport = report
                }).ConfigureAwait(false);

                List<ModelProfileRevision> list = await db.ModelProfiles.ReadRevisionsAsync(profile.Id).ConfigureAwait(false);
                AssertNull(list[0].EvaluationRunId, "observed revision has no run");
                AssertNull(list[0].EvaluationReport, "observed revision has no report");
                ModelProfileRevision read = (await db.ModelProfiles.ReadRevisionAsync(advisory.Id).ConfigureAwait(false))!;
                AssertEqual("mpr_eval", read.EvaluationRunId, "run id");
                AssertNotNull(read.EvaluationReport, "report");
                AssertEqual("One task at a time.", read.EvaluationReport!.Narrative.HowToWork, "note");
                AssertEqual("Strong: reviews (3 of 3).", read.EvaluationReport.Narrative.Strengths.Single().Text, "strength");
                AssertEqual(observed.ProfileId, read.ProfileId, "same profile");
            }));

            cases.Add(CaseAsync("compatibility_symmetric", "Compatibility marks are symmetric, replace the set, clear with an empty list, and never edit a revision", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfile profile = new ModelProfile { Identity = new ModelProfileIdentity { Runtime = "Mux" } };
                await db.ModelProfiles.CreateAsync(profile, "key-c").ConfigureAwait(false);
                ModelProfileRevision r1 = await db.ModelProfiles.CreateRevisionAsync(new ModelProfileRevision { ProfileId = profile.Id, RuntimeVersion = "1.0.0", FirstSeenUtc = _Fixed }).ConfigureAwait(false);
                ModelProfileRevision r2 = await db.ModelProfiles.CreateRevisionAsync(new ModelProfileRevision { ProfileId = profile.Id, RuntimeVersion = "2.0.0", FirstSeenUtc = _Fixed.AddMinutes(1) }).ConfigureAwait(false);
                ModelProfileRevision r3 = await db.ModelProfiles.CreateRevisionAsync(new ModelProfileRevision { ProfileId = profile.Id, RuntimeVersion = "3.0.0", FirstSeenUtc = _Fixed.AddMinutes(2) }).ConfigureAwait(false);

                await db.ModelProfiles.SetCompatibleWithAsync(r1.Id, new[] { r2.Id, r3.Id, r2.Id, r1.Id }).ConfigureAwait(false);
                List<ModelProfileRevision> list = await db.ModelProfiles.ReadRevisionsAsync(profile.Id).ConfigureAwait(false);
                AssertEqual(2, list[0].CompatibleWith.Count, "r1 has r2 and r3 (duplicates and itself dropped)");
                AssertTrue(list[1].CompatibleWith.SequenceEqual(new[] { r1.Id }), "r2 has r1");
                AssertTrue(list[2].CompatibleWith.SequenceEqual(new[] { r1.Id }), "r3 has r1");
                AssertEqual("2.0.0", list[1].RuntimeVersion, "revision unchanged");
                AssertSameInstant(_Fixed.AddMinutes(1), list[1].FirstSeenUtc, "first seen unchanged");

                await db.ModelProfiles.SetCompatibleWithAsync(r2.Id, new[] { r3.Id }).ConfigureAwait(false);
                ModelProfileRevision read1 = (await db.ModelProfiles.ReadRevisionAsync(r1.Id).ConfigureAwait(false))!;
                AssertTrue(read1.CompatibleWith.SequenceEqual(new[] { r3.Id }), "r1 lost r2 when r2's set was replaced");
                ModelProfileRevision read3 = (await db.ModelProfiles.ReadRevisionAsync(r3.Id).ConfigureAwait(false))!;
                AssertEqual(2, read3.CompatibleWith.Count, "r3 has r1 and r2");

                await db.ModelProfiles.SetCompatibleWithAsync(r3.Id, new string[0]).ConfigureAwait(false);
                list = await db.ModelProfiles.ReadRevisionsAsync(profile.Id).ConfigureAwait(false);
                AssertTrue(list.All(r => r.CompatibleWith.Count == 0), "empty list clears every mark involving r3");
            }));

            cases.Add(CaseAsync("revisions_bounded_newest_kept", "A profile read returns the newest 500 of 501 revisions with Measured from every revision; the scorecard groups only the newest 500", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfile profile = new ModelProfile { Identity = new ModelProfileIdentity { Runtime = "Mux" } };
                await db.ModelProfiles.CreateAsync(profile, "key-many").ConfigureAwait(false);
                List<ModelProfileRevision> created = new List<ModelProfileRevision>();
                for (int i = 0; i <= ModelProfile.MaxRevisions; i++)
                {
                    ModelProfileRevision revision = new ModelProfileRevision { ProfileId = profile.Id, RuntimeVersion = i + ".0.0", FirstSeenUtc = _Fixed.AddMinutes(i) };
                    if (i == 0)
                    {
                        revision.EvaluationRunId = "mpr_oldest";
                        revision.EvaluationReport = new ModelEvaluationReport { RunId = "mpr_oldest", Complete = true, ProfileId = profile.Id };
                    }
                    created.Add(await db.ModelProfiles.CreateRevisionAsync(revision).ConfigureAwait(false));
                }

                ModelProfileAccessService access = new ModelProfileAccessService(db, QuietLogging());
                AuthContext admin = new AuthContext { IsAuthenticated = true, IsAdmin = true, UserId = "usr_admin" };
                ModelProfile read = await access.GetAsync(admin, profile.Id).ConfigureAwait(false);
                AssertEqual(ModelProfile.MaxRevisions, read.Revisions.Count, "bounded to the limit");
                AssertEqual(created[1].Id, read.Revisions[0].Id, "the oldest revision is the one dropped");
                AssertEqual(created[created.Count - 1].Id, read.Revisions[read.Revisions.Count - 1].Id, "newest kept, oldest first");
                AssertNotNull(read.Measured, "measured still read from every revision");
                AssertEqual(created[0].Id, read.Measured!.RevisionId, "measured names the dropped revision's report");
                CollectionBounds.Validate(read);

                ModelScorecard card = await new ModelScorecardService(db, QuietLogging()).ComputeAsync(profile, new ModelScorecardRequest { Persona = "Worker" }, _Fixed.AddDays(1)).ConfigureAwait(false);
                List<string> pooled = card.Groups.SelectMany(g => g.RevisionIds).ToList();
                AssertEqual(ModelProfile.MaxRevisions, pooled.Count, "scorecard pools the newest revisions");
                AssertFalse(pooled.Contains(created[0].Id), "the oldest revision is not grouped");
                CollectionBounds.Validate(card);
            }));

            cases.Add(CaseAsync("compatibility_marks_bounded", "A revision with 100 marks cannot gain another: an admin mark is rejected, a request over 1000 ids is rejected, and a Full Evaluate report pools through the newest revision with room", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfile profile = new ModelProfile { Identity = new ModelProfileIdentity { Runtime = "Mux" } };
                await db.ModelProfiles.CreateAsync(profile, "key-full").ConfigureAwait(false);
                ModelProfileRevision basis = await db.ModelProfiles.CreateRevisionAsync(new ModelProfileRevision { ProfileId = profile.Id, RuntimeVersion = "1.0.0", FirstSeenUtc = _Fixed }).ConfigureAwait(false);
                List<string> others = new List<string>();
                for (int i = 0; i < ModelProfileRevision.MaxCompatibleWith; i++)
                    others.Add((await db.ModelProfiles.CreateRevisionAsync(new ModelProfileRevision { ProfileId = profile.Id, RuntimeVersion = "2." + i + ".0", FirstSeenUtc = _Fixed.AddMinutes(i + 1) }).ConfigureAwait(false)).Id);
                await db.ModelProfiles.SetCompatibleWithAsync(basis.Id, others).ConfigureAwait(false);
                ModelProfileRevision outsider = await db.ModelProfiles.CreateRevisionAsync(new ModelProfileRevision { ProfileId = profile.Id, RuntimeVersion = "9.0.0", FirstSeenUtc = _Fixed.AddDays(1) }).ConfigureAwait(false);

                ModelProfileAccessService access = new ModelProfileAccessService(db, QuietLogging());
                AuthContext admin = new AuthContext { IsAuthenticated = true, IsAdmin = true, UserId = "usr_admin" };
                bool rejected = false;
                try { await access.SetCompatibleWithAsync(admin, profile.Id, outsider.Id, new ModelProfileCompatibilityRequest { CompatibleWith = new List<string> { basis.Id } }).ConfigureAwait(false); }
                catch (ArgumentException ex) { rejected = ex.Message.Contains(basis.Id, StringComparison.Ordinal); }
                AssertTrue(rejected, "marking a full revision is rejected, naming it");
                AssertEqual(ModelProfileRevision.MaxCompatibleWith, (await db.ModelProfiles.ReadRevisionAsync(basis.Id).ConfigureAwait(false))!.CompatibleWith.Count, "basis unchanged");
                AssertEqual(0, (await db.ModelProfiles.ReadRevisionAsync(outsider.Id).ConfigureAwait(false))!.CompatibleWith.Count, "nothing stored");

                ModelProfileRevision resaved = await access.SetCompatibleWithAsync(admin, profile.Id, others[0], new ModelProfileCompatibilityRequest { CompatibleWith = new List<string> { basis.Id } }).ConfigureAwait(false);
                AssertTrue(resaved.CompatibleWith.SequenceEqual(new[] { basis.Id }), "re-saving an existing mark to a full revision is allowed");

                List<string> tooMany = Enumerable.Range(0, CollectionLimits.MaxRequestItems + 1).Select(i => basis.Id).ToList();
                bool limited = false;
                try { await access.SetCompatibleWithAsync(admin, profile.Id, outsider.Id, new ModelProfileCompatibilityRequest { CompatibleWith = tooMany }).ConfigureAwait(false); }
                catch (CollectionLimitExceededException ex) { limited = ex.Field == "CompatibleWith"; }
                AssertTrue(limited, "a request over the request item limit is rejected");

                ModelProfileService service = new ModelProfileService(db, QuietLogging());
                ModelProfileRevision basisRead = (await db.ModelProfiles.ReadRevisionAsync(basis.Id).ConfigureAwait(false))!;
                ModelEvaluationReport report = new ModelEvaluationReport { RunId = "mpr_full", Complete = true };
                ModelProfileRevision advisory = await service.RecordEvaluationAsync(new ModelProfileResolution { Profile = profile, Revision = basisRead }, report).ConfigureAwait(false);
                AssertTrue(advisory.CompatibleWith.SequenceEqual(new[] { others[others.Count - 1] }), "pooled through the newest revision of the basis's pool");
                AssertEqual(ModelProfileRevision.MaxCompatibleWith, (await db.ModelProfiles.ReadRevisionAsync(basis.Id).ConfigureAwait(false))!.CompatibleWith.Count, "basis not pushed over the limit");
                foreach (ModelProfileRevision revision in await db.ModelProfiles.ReadRevisionsAsync(profile.Id).ConfigureAwait(false))
                    AssertTrue(revision.CompatibleWith.Count <= ModelProfileRevision.MaxCompatibleWith, revision.Id + " within the limit");

                ModelProfileRevision fresh = await db.ModelProfiles.CreateRevisionAsync(new ModelProfileRevision { ProfileId = profile.Id, RuntimeVersion = "10.0.0", FirstSeenUtc = _Fixed.AddDays(2) }).ConfigureAwait(false);
                ModelProfileRevision second = await service.RecordEvaluationAsync(new ModelProfileResolution { Profile = profile, Revision = fresh }, new ModelEvaluationReport { RunId = "mpr_room", Complete = true }).ConfigureAwait(false);
                AssertTrue(second.CompatibleWith.SequenceEqual(new[] { fresh.Id }), "a basis with room is marked directly");
            }));

            cases.Add(CaseAsync("captain_reference_roundtrip", "Captain.ModelProfileId round-trips through LinkCaptainAsync, survives a full captain update, and serializes on the API model", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Captain captain = await db.Captains.CreateAsync(new Captain { Name = "profiled", Runtime = AgentRuntimeEnum.Codex }).ConfigureAwait(false);
                AssertNull((await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!.ModelProfileId, "new captain has no profile");

                AssertTrue(await db.ModelProfiles.LinkCaptainAsync(captain.Id, "mdl_linked").ConfigureAwait(false), "linked");
                Captain read = (await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!;
                AssertEqual("mdl_linked", read.ModelProfileId, "round trip");

                // A stale captain instance saved later never clears the server-owned reference.
                captain.Name = "renamed";
                captain.ModelProfileId = null;
                await db.Captains.UpdateAsync(captain).ConfigureAwait(false);
                Captain afterUpdate = (await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!;
                AssertEqual("renamed", afterUpdate.Name, "update applied");
                AssertEqual("mdl_linked", afterUpdate.ModelProfileId, "reference kept");

                string json = JsonSerializer.Serialize(afterUpdate, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                AssertContains("\"modelProfileId\":\"mdl_linked\"", json);

                Captain merged = EntityUpdateMerger.MergeCaptain(afterUpdate, new Captain { Name = "client", Runtime = AgentRuntimeEnum.Codex, ModelProfileId = "mdl_forged" });
                AssertEqual("mdl_linked", merged.ModelProfileId, "a client update cannot set the reference");

                AssertTrue(await db.ModelProfiles.LinkCaptainAsync(captain.Id, null).ConfigureAwait(false), "cleared");
                AssertNull((await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!.ModelProfileId, "cleared");
                AssertFalse(await db.ModelProfiles.LinkCaptainAsync("cpt_missing", "mdl_x").ConfigureAwait(false), "missing captain");
            }));

            cases.Add(CaseAsync("rules_runtime_major", "RuntimeMajor reads the first number of the first dotted version", TestTags.Positive, () =>
            {
                AssertEqual("1", ModelProfileRules.RuntimeMajor("1.4.1"));
                AssertEqual("2", ModelProfileRules.RuntimeMajor("v2.0.14 (Claude Code)"));
                AssertEqual("2", ModelProfileRules.RuntimeMajor("codex-cli 02.1"));
                AssertEqual("0", ModelProfileRules.RuntimeMajor("0.9"));
                AssertEqual("nightly", ModelProfileRules.RuntimeMajor(" Nightly "));
                AssertNull(ModelProfileRules.RuntimeMajor(null), "null");
                AssertNull(ModelProfileRules.RuntimeMajor("  "), "blank");

                ModelProfileIdentity a = new ModelProfileIdentity { Runtime = "Mux", Provider = "Ollama", Model = "Qwen3", ServedContextTokens = 8192 };
                ModelProfileIdentity b = new ModelProfileIdentity { Runtime = " mux ", Provider = "ollama", Model = "qwen3 ", ServedContextTokens = 8192 };
                AssertEqual(ModelProfileRules.IdentityKey("ten_1", a), ModelProfileRules.IdentityKey("ten_1", b), "case and whitespace insensitive");
                AssertEqual(64, ModelProfileRules.IdentityKey(null, a).Length, "sha-256 hex");
                AssertNotEqual(ModelProfileRules.IdentityKey("ten_1", a), ModelProfileRules.IdentityKey("ten_2", a), "tenant is part of the key");
                b.ServedContextTokens = 16384;
                AssertNotEqual(ModelProfileRules.IdentityKey("ten_1", a), ModelProfileRules.IdentityKey("ten_1", b), "served context is part of the key");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("resolve_idempotent", "The same identity twice gives one profile and one revision; the captain is linked", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfileService service = new ModelProfileService(db, QuietLogging());
                Captain captain = await db.Captains.CreateAsync(new Captain { Name = "obs", Runtime = AgentRuntimeEnum.Mux }).ConfigureAwait(false);

                ModelProfileResolution first = await service.ResolveAsync(Observation("1.4.1", captain.Id)).ConfigureAwait(false);
                AssertTrue(first.ProfileCreated, "profile created");
                AssertTrue(first.RevisionCreated, "revision created");
                AssertEqual("ten_i", first.Profile.TenantId, "tenant");
                AssertEqual("qwen3.8:27b", first.Profile.Identity.Model, "model");
                AssertEqual("1.4.1", first.Revision.RuntimeVersion, "version");
                AssertEqual("sha256:aaa", first.Revision.ModelDigest, "digest");
                AssertEqual((double?)0.1, first.Revision.Sampling.Temperature, "temperature");
                AssertEqual((bool?)true, first.Revision.ToolConfiguration.Tools, "tools");

                ModelProfileObservation again = Observation("1.4.1", captain.Id);
                again.Runtime = " mux ";
                again.Model = "QWEN3.8:27B";
                ModelProfileResolution second = await service.ResolveAsync(again).ConfigureAwait(false);
                AssertFalse(second.ProfileCreated, "no second profile");
                AssertFalse(second.RevisionCreated, "no second revision");
                AssertEqual(first.Profile.Id, second.Profile.Id, "same profile");
                AssertEqual(first.Revision.Id, second.Revision.Id, "same revision");
                AssertEqual(1L, (await db.ModelProfiles.EnumerateAsync(new ModelProfileQuery()).ConfigureAwait(false)).TotalRecords, "one profile");
                AssertEqual(1, (await db.ModelProfiles.ReadRevisionsAsync(first.Profile.Id).ConfigureAwait(false)).Count, "one revision");
                AssertEqual(first.Profile.Id, (await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!.ModelProfileId, "captain linked");

                ModelProfileObservation otherTenant = Observation("1.4.1", null);
                otherTenant.TenantId = "ten_j";
                ModelProfileResolution other = await service.ResolveAsync(otherTenant).ConfigureAwait(false);
                AssertNotEqual(first.Profile.Id, other.Profile.Id, "profiles are per tenant");
            }));

            cases.Add(CaseAsync("resolve_concurrent", "Concurrent observations of a new identity make one profile and one revision", TestTags.Reliability, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfileService one = new ModelProfileService(db, QuietLogging());
                ModelProfileService two = new ModelProfileService(db, QuietLogging());
                List<Task<ModelProfileResolution>> tasks = new List<Task<ModelProfileResolution>>();
                for (int i = 0; i < 8; i++) tasks.Add((i % 2 == 0 ? one : two).ResolveAsync(Observation("1.4." + i, null)));
                ModelProfileResolution[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
                AssertEqual(1, results.Select(r => r.Profile.Id).Distinct().Count(), "one profile");
                AssertEqual(1, results.Select(r => r.Revision.Id).Distinct().Count(), "patch releases pooled into one revision");
                AssertEqual(1, results.Count(r => r.ProfileCreated), "created once");
            }));

            cases.Add(CaseAsync("revision_pooling_rules", "A runtime patch or minor release pools; a new major version, digest, served context, temperature or top-p starts a new revision", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfileService service = new ModelProfileService(db, QuietLogging());

                ModelProfileResolution baseline = await service.ResolveAsync(Observation("1.4.1", null)).ConfigureAwait(false);
                string profileId = baseline.Profile.Id;

                ModelProfileResolution patch = await service.ResolveAsync(Observation("1.4.2", null)).ConfigureAwait(false);
                AssertFalse(patch.RevisionCreated, "patch release pools");
                AssertEqual(baseline.Revision.Id, patch.Revision.Id, "patch: same revision");
                AssertEqual("1.4.1", patch.Revision.RuntimeVersion, "the pooled revision keeps its first-seen version");
                ModelProfileResolution minor = await service.ResolveAsync(Observation("1.5.0", null)).ConfigureAwait(false);
                AssertEqual(baseline.Revision.Id, minor.Revision.Id, "minor release pools");

                ModelProfileObservation unknown = Observation(null, null);
                unknown.ModelDigest = null;
                unknown.Temperature = null;
                unknown.TopP = null;
                AssertEqual(baseline.Revision.Id, (await service.ResolveAsync(unknown).ConfigureAwait(false)).Revision.Id, "unknown values pool");

                ModelProfileResolution major = await service.ResolveAsync(Observation("2.0.0", null)).ConfigureAwait(false);
                AssertTrue(major.RevisionCreated, "major version starts a revision");
                AssertEqual(profileId, major.Profile.Id, "same profile");
                AssertEqual("2.0.0", major.Revision.RuntimeVersion);
                AssertEqual(major.Revision.Id, (await service.ResolveAsync(Observation("2.0.1", null)).ConfigureAwait(false)).Revision.Id, "patch of the new major pools into it");

                ModelProfileObservation digest = Observation("2.0.1", null);
                digest.ModelDigest = "sha256:bbb";
                ModelProfileResolution byDigest = await service.ResolveAsync(digest).ConfigureAwait(false);
                AssertTrue(byDigest.RevisionCreated, "new digest starts a revision");
                AssertEqual("sha256:bbb", byDigest.Revision.ModelDigest);
                AssertEqual(byDigest.Revision.Id, (await service.ResolveAsync(digest).ConfigureAwait(false)).Revision.Id, "same digest again pools");

                ModelProfileObservation temperature = Observation("2.0.1", null);
                temperature.ModelDigest = "sha256:bbb";
                temperature.Temperature = 0.7;
                ModelProfileResolution byTemperature = await service.ResolveAsync(temperature).ConfigureAwait(false);
                AssertTrue(byTemperature.RevisionCreated, "new temperature starts a revision");
                AssertEqual((double?)0.7, byTemperature.Revision.Sampling.Temperature);

                ModelProfileObservation topP = Observation("2.0.1", null);
                topP.ModelDigest = "sha256:bbb";
                topP.Temperature = 0.7;
                topP.TopP = 0.5;
                ModelProfileResolution byTopP = await service.ResolveAsync(topP).ConfigureAwait(false);
                AssertTrue(byTopP.RevisionCreated, "new top-p starts a revision");

                List<ModelProfileRevision> revisions = await db.ModelProfiles.ReadRevisionsAsync(profileId).ConfigureAwait(false);
                AssertEqual(5, revisions.Count, "baseline, major, digest, temperature, top-p");
                AssertEqual(baseline.Revision.Id, revisions[0].Id, "the first revision is still there");
                AssertEqual("1.4.1", revisions[0].RuntimeVersion, "and unchanged");
                AssertEqual("sha256:aaa", revisions[0].ModelDigest, "and unchanged");

                ModelProfileObservation context = Observation("2.0.1", null);
                context.ServedContextTokens = 65536;
                ModelProfileResolution byContext = await service.ResolveAsync(context).ConfigureAwait(false);
                AssertTrue(byContext.ProfileCreated, "a new served context is a new identity");
                AssertTrue(byContext.RevisionCreated, "with its own, unpooled revision");
                AssertNotEqual(profileId, byContext.Profile.Id, "different profile");
                AssertNotEqual(byTopP.Revision.Id, byContext.Revision.Id, "different revision");
            }));

            cases.Add(CaseAsync("try_resolve_best_effort", "TryResolveAsync never throws: no runtime or a database failure gives null", TestTags.Negative, async () =>
            {
                TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfileService service = new ModelProfileService(db, QuietLogging());
                AssertNull(await service.TryResolveAsync(null).ConfigureAwait(false), "null observation");
                AssertNull(await service.TryResolveAsync(new ModelProfileObservation { Runtime = "  " }).ConfigureAwait(false), "no runtime");
                await AssertThrowsAsync<ArgumentException>(() => service.ResolveAsync(new ModelProfileObservation()), "ResolveAsync rejects no runtime").ConfigureAwait(false);
                AssertNull(ModelProfileService.ObservationFrom((StageOutcome?)null), "no outcome");
                AssertNull(ModelProfileService.ObservationFrom(new StageOutcome()), "outcome without runtime");
                testDb.Dispose();
                // A SQLite test database is deleted on dispose, so the next call fails inside the service; the shared
                // server database outlives the case, so the failure path is only exercised on SQLite.
                if (TestDatabaseConfig.IsSqlite)
                    AssertNull(await service.TryResolveAsync(Observation("1.0.0", null)).ConfigureAwait(false), "a database failure is swallowed");
            }));

            cases.Add(CaseAsync("stage_outcome_links_captain", "Recording a stage outcome finds or creates the captain's profile and points the captain at it; a second attempt pools", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Captain captain = await db.Captains.CreateAsync(new Captain { Name = "vendor-cli", Runtime = AgentRuntimeEnum.Codex, Model = "gpt-5" }).ConfigureAwait(false);
                Mission mission = await db.Missions.CreateAsync(new Mission("profiled mission", "d")).ConfigureAwait(false);
                StageOutcomeService outcomes = new StageOutcomeService(db, QuietLogging());

                StageOutcome? first = await outcomes.RecordAttemptAsync(new StageAttemptCapture { Claimed = true, MissionId = mission.Id, Captain = captain, ExitCode = 0 }).ConfigureAwait(false);
                AssertNotNull(first, "outcome recorded");
                string? profileId = (await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!.ModelProfileId;
                AssertNotNull(profileId, "the vendor CLI captain is profiled");
                ModelProfile profile = (await db.ModelProfiles.ReadAsync(profileId!).ConfigureAwait(false))!;
                AssertEqual("Codex", profile.Identity.Runtime, "runtime");
                AssertEqual("gpt-5", profile.Identity.Model, "model");
                AssertNull(profile.Identity.Provider, "a vendor CLI has no provider");

                await outcomes.RecordAttemptAsync(new StageAttemptCapture { Claimed = true, MissionId = mission.Id, Captain = captain, ExitCode = 0 }).ConfigureAwait(false);
                AssertEqual(1L, (await db.ModelProfiles.EnumerateAsync(new ModelProfileQuery()).ConfigureAwait(false)).TotalRecords, "one profile");
                AssertEqual(1, (await db.ModelProfiles.ReadRevisionsAsync(profileId!).ConfigureAwait(false)).Count, "one revision");
                AssertNull((await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!.Tier, "the profile does not set the tier");
            }));

            cases.Add(CaseAsync("observation_mapping", "Stage outcome and probe run provenance map onto an observation", TestTags.Positive, () =>
            {
                StageOutcome outcome = new StageOutcome
                {
                    TenantId = "ten_o", CaptainId = "cpt_o", Runtime = AgentRuntimeEnum.ApiEndpoint, ModelProvider = "Ollama", Model = "qwen",
                    ContextTokens = 32768, RuntimeVersion = "0.5.1", ModelDigest = "sha256:o", Temperature = 0.2, TopP = 0.9,
                    ToolsEnabled = true, McpToolsEnabled = false
                };
                ModelProfileObservation fromOutcome = ModelProfileService.ObservationFrom(outcome)!;
                AssertEqual("ApiEndpoint", fromOutcome.Runtime);
                AssertEqual("cpt_o", fromOutcome.CaptainId);
                AssertEqual("ten_o", fromOutcome.TenantId);
                AssertEqual((int?)32768, fromOutcome.ServedContextTokens);
                AssertEqual("0.5.1", fromOutcome.RuntimeVersion);
                AssertEqual((double?)0.9, fromOutcome.TopP);
                AssertEqual((bool?)false, fromOutcome.McpToolsEnabled);

                ModelProbeRun run = new ModelProbeRun
                {
                    TenantId = "ten_p", TargetKind = ModelProbeTargetEnum.Captain, CaptainId = "cpt_p", Runtime = "Codex",
                    Model = "gpt-5", ConfiguredContextTokens = 8192, ServedContextTokens = 4096, RuntimeVersion = "1.0", ModelDigest = null
                };
                ModelProfileObservation fromRun = ModelProfileService.ObservationFrom(run, null)!;
                AssertEqual("cpt_p", fromRun.CaptainId);
                AssertEqual((int?)4096, fromRun.ServedContextTokens, "measured served context wins");
                AssertNull(fromRun.Temperature, "no endpoint, no sampling");
                run.ServedContextTokens = null;
                run.TargetKind = ModelProbeTargetEnum.ModelEndpoint;
                ModelEndpoint endpoint = new ModelEndpoint { Model = "gpt-5" };
                endpoint.InferenceOptions.Temperature = 0.3;
                ModelProfileObservation endpointRun = ModelProfileService.ObservationFrom(run, endpoint)!;
                AssertNull(endpointRun.CaptainId, "an endpoint run links no captain");
                AssertEqual((int?)8192, endpointRun.ServedContextTokens, "configured context otherwise");
                AssertEqual((double?)0.3, endpointRun.Temperature, "endpoint sampling");
                AssertNull(ModelProfileService.ObservationFrom(new ModelProbeRun(), null), "run without runtime");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Database: model profiles and revisions",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static ModelProfileObservation Observation(string? runtimeVersion, string? captainId)
        {
            return new ModelProfileObservation
            {
                TenantId = "ten_i",
                CaptainId = captainId,
                Runtime = "Mux",
                Provider = "Ollama",
                Model = "qwen3.8:27b",
                ServedContextTokens = 131072,
                RuntimeVersion = runtimeVersion,
                ModelDigest = "sha256:aaa",
                Temperature = 0.1,
                TopP = null,
                ToolsEnabled = true,
                McpToolsEnabled = false
            };
        }

        private static LoggingModule QuietLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static void AssertSameInstant(DateTime expected, DateTime actual, string label)
        {
            double delta = Math.Abs((expected.ToUniversalTime() - actual.ToUniversalTime()).TotalMilliseconds);
            AssertTrue(delta < 1000, label + ": expected " + expected.ToString("o") + " got " + actual.ToString("o"));
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag, TestTags.Database });
        }

        #endregion
    }
}
