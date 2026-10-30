namespace Test.Shared.Suites.Models
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The black box schema contract (docs/BLACKBOX_SCHEMA.md): docs/blackbox-record.schema.json equals what the models
    /// generate (drift check, like the API surface), captured records and published shapes validate against it, readers
    /// ignore unknown fields and read unknown enum values as Unknown, and a record missing a required field fails.
    /// </summary>
    public sealed class BlackBoxSchemaSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Models.BlackBoxSchema";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("schema_matches_models", "The committed schema file equals the schema generated from the models (run scripts/common/generate-blackbox-schema.sh after a model change)", TestTags.Positive, () =>
            {
                string path = Path.Combine(DashboardSource.RepoRoot(), "docs", "blackbox-record.schema.json");
                AssertTrue(File.Exists(path), "docs/blackbox-record.schema.json exists");
                string committed = File.ReadAllText(path).Replace("\r\n", "\n");
                string generated = BlackBoxSchemaGenerator.GenerateJson();
                if (committed != generated)
                {
                    string[] a = committed.Split('\n');
                    string[] b = generated.Split('\n');
                    int line = 0;
                    while (line < a.Length && line < b.Length && a[line] == b[line]) line++;
                    throw new AssertionException("docs/blackbox-record.schema.json is out of date; run scripts/common/generate-blackbox-schema.sh. First difference at line " + (line + 1) + ": committed '" + (line < a.Length ? a[line].Trim() : "<end>") + "', generated '" + (line < b.Length ? b[line].Trim() : "<end>") + "'.");
                }
                return Task.CompletedTask;
            }));

            cases.Add(Case("schema_shape", "The schema declares draft 2020-12, the schema version, bounds from [CollectionBound], string caps and enum values", TestTags.Positive, () =>
            {
                BlackBoxJsonSchemaNode root = BlackBoxSchemaGenerator.Generate();
                AssertEqual("https://json-schema.org/draft/2020-12/schema", root.Schema);
                AssertEqual((int?)BlackBoxLimits.SchemaVersion, root.ArmadaSchemaVersion);
                BlackBoxJsonSchemaNode conflict = root.Defs!["BlackBoxConflict"];
                AssertEqual((int?)BlackBoxLimits.MaxConflictedFiles, conflict.Properties!["conflictedFiles"].MaxItems, "maxItems from [CollectionBound]");
                AssertEqual((int?)BlackBoxLimits.MaxPathChars, conflict.Properties["conflictedFiles"].Items!.MaxLength, "path cap");
                AssertEqual((int?)BlackBoxLimits.MaxRawReasonChars, root.Defs["BlackBoxClassification"].Properties!["rawReason"].MaxLength, "reason cap");
                AssertTrue(root.Defs["BlackBoxSite"].Properties!["step"].Enum!.Contains("ScopeViolation"), "step enum values");
                AssertTrue(root.Defs["BlackBoxRecord"].Required!.Contains("entryId"), "entryId required");
                AssertFalse(root.Defs["BlackBoxRecord"].Required!.Contains("analysis"), "nullable sections optional");
                foreach (Type shape in BlackBoxSchemaGenerator.Shapes) AssertTrue(root.Defs.ContainsKey(shape.Name), shape.Name + " in $defs");
                AssertTrue(root.Defs["BlackBoxEntry"].Properties!.ContainsKey("record"), "entry carries the record"); AssertFalse(root.Defs.ContainsKey("BlackBoxObject"), "the internal object row (with its BLOB) is not published");
                AssertTrue(root.Defs.Values.All(d => d.Description != null), "every definition is described");
                return Task.CompletedTask;
            }));

            cases.Add(Case("captured_record_validates", "A record written by a real capture, and the entry and summary shapes, validate against the schema", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                mission.AgentOutput = "output";
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.WorkProduced, BlackBoxStepEnum.TargetIntegration, c =>
                {
                    c.GitFacts = new BlackBoxGitFacts { ConflictedFiles = new List<string> { "src/a.cs" }, DirtyPaths = new List<string> { "b.txt" } };
                    c.Exception = new InvalidOperationException("boom");
                    c.CheckResults.Add(new StageCheckResult { Status = StageCheckStatusEnum.Failed, FailedCommand = "dotnet test", ExitCode = 1 });
                }).ConfigureAwait(false))!;
                await bb.Queue.DrainAsync().ConfigureAwait(false);
                BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
                byte[] recordJson = (await bb.Objects.ReadAsync(entry, BlackBoxObjectNames.Record).ConfigureAwait(false)).Data;
                BlackBoxJsonSchemaNode root = BlackBoxSchemaGenerator.Generate();
                List<string> errors = BlackBoxSchemaValidator.Validate(JsonNode.Parse(recordJson), root);
                AssertEqual(0, errors.Count, "record.json validates: " + String.Join("; ", errors.Take(5)));
                entry.Record = BlackBoxJson.DeserializeRecord(recordJson);
                AssertEqual(0, BlackBoxSchemaValidator.ValidateDef(JsonNode.Parse(JsonSerializer.Serialize(entry, BlackBoxJson.Options)), root, "BlackBoxEntry").Count, "entry with record validates");
                AssertEqual(0, BlackBoxSchemaValidator.ValidateDef(JsonNode.Parse(JsonSerializer.Serialize(BlackBoxService.Summarize(entry), BlackBoxJson.Options)), root, "BlackBoxEntrySummary").Count, "summary validates");
                BlackBoxObjectChunk chunk = await bb.Service.ReadObjectChunkAsync(BlackBoxTestKit.Admin(), id, BlackBoxObjectNames.Brief).ConfigureAwait(false);
                AssertEqual(0, BlackBoxSchemaValidator.ValidateDef(JsonNode.Parse(JsonSerializer.Serialize(chunk, BlackBoxJson.Options)), root, "BlackBoxObjectChunk").Count, "chunk validates");
            }));

            cases.Add(Case("tolerant_reads", "Unknown properties are ignored; unknown enum values read as Unknown, or null for a nullable enum without an Unknown member", TestTags.Positive, () =>
            {
                BlackBoxRecord sample = new BlackBoxRecord { EntryId = "bbx_tol", CreatedUtc = new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc) };
                sample.Mechanism = new BlackBoxMechanism { RuntimeFailureKind = RuntimeFailureKindEnum.Crash };
                sample.Classification = new BlackBoxClassification { Kind = MissionFailureKindEnum.Crash };
                JsonObject json = JsonNode.Parse(BlackBoxJson.SerializeRecord(sample))!.AsObject();
                json["futureSection"] = new JsonObject { ["anything"] = 1 };
                json["captureMode"] = "Teleport";
                json["state"] = 42;
                json["mechanism"]!["runtimeFailureKind"] = "QuantumFlux";
                json["mechanism"]!["newField"] = "x";
                json["classification"]!["kind"] = "SomethingNew";
                BlackBoxRecord read = BlackBoxJson.DeserializeRecord(Encoding.UTF8.GetBytes(json.ToJsonString()));
                AssertEqual("bbx_tol", read.EntryId, "known fields read");
                AssertEqual(BlackBoxCaptureModeEnum.Unknown, read.CaptureMode, "unknown name reads as Unknown");
                AssertEqual(BlackBoxStateEnum.Unknown, read.State, "unknown number reads as Unknown");
                AssertNull(read.Mechanism!.RuntimeFailureKind, "nullable enum without Unknown reads as null");
                AssertEqual((MissionFailureKindEnum?)MissionFailureKindEnum.Unknown, read.Classification!.Kind, "nullable enum with Unknown reads as Unknown");
                AssertEqual("\"Crash\"", JsonSerializer.Serialize(RuntimeFailureKindEnum.Crash, BlackBoxJson.Options), "written by name");
                return Task.CompletedTask;
            }));

            cases.Add(Case("missing_required_fails", "A record missing a required field, or carrying a wrong type or an over-limit list, fails validation", TestTags.Negative, () =>
            {
                BlackBoxJsonSchemaNode root = BlackBoxSchemaGenerator.Generate();
                BlackBoxRecord sample = new BlackBoxRecord { EntryId = "bbx_req", CreatedUtc = DateTime.UtcNow };
                JsonObject good = JsonNode.Parse(BlackBoxJson.SerializeRecord(sample))!.AsObject();
                AssertEqual(0, BlackBoxSchemaValidator.Validate(good, root).Count, "baseline validates");
                JsonObject missing = JsonNode.Parse(good.ToJsonString())!.AsObject();
                missing.Remove("entryId");
                AssertTrue(BlackBoxSchemaValidator.Validate(missing, root).Any(e => e.Contains("missing required entryId", StringComparison.Ordinal)), "missing entryId fails");
                JsonObject wrongType = JsonNode.Parse(good.ToJsonString())!.AsObject();
                wrongType["schemaVersion"] = "one";
                AssertTrue(BlackBoxSchemaValidator.Validate(wrongType, root).Count > 0, "wrong type fails");
                JsonObject tooMany = JsonNode.Parse(good.ToJsonString())!.AsObject();
                JsonArray attempts = new JsonArray();
                for (int i = 0; i < BlackBoxLimits.MaxAttempts + 1; i++) attempts.Add(new JsonObject { ["attempt"] = i });
                tooMany["attempts"] = attempts;
                AssertTrue(BlackBoxSchemaValidator.Validate(tooMany, root).Any(e => e.Contains("more than", StringComparison.Ordinal)), "over-limit list fails");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box schema", cases: cases);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }
    }
}
