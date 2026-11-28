namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Shared.Infrastructure;
    using Test.Shared.Infrastructure.ApiSurface;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// RELIABILITY_SIMPLICITY_PLAN.md RS-11.4: source scans that keep the Phase 3 to 5 simplifications in place. Nothing
    /// reads control markers back from a mission description or finds Architect slices by title (RS-3.2 to RS-3.4),
    /// Armada.Core does not decide by matching exception or error message text (RS-5.11), and only the Admiral
    /// dispatcher runs a dispatch pass (RS-4.5), and no type is a partial class (RS-11.1). Direct mission status writes are scanned by
    /// <c>Services.MissionTransitions</c> <c>no_direct_status_writes</c> (RS-3.6). The scans match patterns across whole
    /// projects and allow owners by the class a file declares, so moving code between files does not break them.
    /// </summary>
    public sealed class SourceGuardsSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("no_control_markers_read_from_description", "No code reads legacy control markers or matches mission description text, and no slice is found by title", () =>
            {
                List<string> offenders = new List<string>();
                foreach (SourceFile file in Sources(_ProductProjects))
                {
                    // The migration 133 backfill is the one reader of the old markers, and the escalation renderer writes
                    // the prompt note that nothing reads back.
                    bool markerOwner = file.Declares("MissionTypedStateSchema") || file.Declares("EscalationMemoryRenderer");
                    // Prompt composition of the rework section, and Gemini tool descriptions (not missions).
                    bool descriptionOwner = file.Declares("ReworkPromptBuilder") || file.Declares("GeminiBuiltInToolInventoryReader");
                    for (int i = 0; i < file.Code.Length; i++)
                    {
                        string code = file.Code[i];
                        if (!markerOwner && IsLegacyMarker(code)) offenders.Add(file.Location(i));
                        if (!descriptionOwner && IsDescriptionMatch(code) && !IsAllowedDescriptionMatch(code)) offenders.Add(file.Location(i));
                        if (IsTitleLookup(code)) offenders.Add(file.Location(i));
                    }
                }

                AssertEqual(0, offenders.Count, "control state read from description or title text (use the typed mission columns and MissionNotes):\n" + String.Join("\n", offenders));
                AssertTrue(IsLegacyMarker("            if (mission.Description.Contains(\"## Escalated to \")) return;"), "the scan recognizes the escalation heading");
                AssertTrue(IsLegacyMarker("            bool done = text.Contains(MissionTypedStateSchema.LegacyFanOutCompleteMarker);"), "the scan recognizes a legacy marker constant");
                AssertTrue(IsDescriptionMatch("            if (worker.Description?.IndexOf(\"<!-- x -->\", StringComparison.Ordinal) >= 0) return;"), "the scan recognizes a description match");
                AssertTrue(IsTitleLookup("            Mission? slice = all.FirstOrDefault(m => m.Title == parsed.Title + \" [Worker]\");"), "the scan recognizes a title lookup");
                AssertFalse(IsDescriptionMatch("            string description = mission.Description ?? String.Empty;"), "reading a description is not a match");
                return Task.CompletedTask;
            }));

            cases.Add(Case("no_exception_message_matching_in_core", "Armada.Core decides by exception type, provider error code or exit status, not by message text", () =>
            {
                List<string> offenders = new List<string>();
                foreach (SourceFile file in Sources(new[] { "Armada.Core" }))
                    for (int i = 0; i < file.Code.Length; i++)
                        if (IsMessageMatch(file.Code[i]) && !IsAllowedMessageMatch(file.Code[i])) offenders.Add(file.Location(i));

                AssertEqual(0, offenders.Count, "message text matching in Armada.Core (use a typed exception, a provider error code or the exit status):\n" + String.Join("\n", offenders));
                AssertTrue(IsMessageMatch("            catch (DbException ex) when (ex.Message.Contains(\"unique\", StringComparison.OrdinalIgnoreCase))"), "the scan recognizes ex.Message.Contains");
                AssertTrue(IsMessageMatch("            if (inner?.Message?.IndexOf(\"identity\") >= 0) return;"), "the scan recognizes a null-conditional IndexOf");
                AssertTrue(IsMessageMatch("            if (Regex.IsMatch(ex.Message, \"timed out\")) return;"), "the scan recognizes a regex over a message");
                AssertFalse(IsMessageMatch("            _Logging.Warn(_Header + ex.Message);"), "logging a message is not a match");
                return Task.CompletedTask;
            }));

            cases.Add(Case("one_dispatcher", "Only the Admiral dispatcher opens a dispatch pass or assigns the Pending queue", () =>
            {
                List<string> offenders = new List<string>();
                foreach (SourceFile file in Sources(_ProductProjects))
                {
                    if (file.Declares("AdmiralService")) continue;
                    bool enumeratesPending = false;
                    bool assigns = false;
                    for (int i = 0; i < file.Code.Length; i++)
                    {
                        string code = file.Code[i];
                        // The MissionService facade forwards IMissionService members to the class that owns them.
                        if (file.Declares("MissionService") && _FacadeForward.IsMatch(code)) continue;
                        if (_BeginDispatchPassCall.IsMatch(code) && !_MethodDeclaration.IsMatch(code)) offenders.Add(file.Location(i) + " (dispatch pass)");
                        if (_DispatchPendingDeclaration.IsMatch(code)) offenders.Add(file.Location(i) + " (second dispatcher)");
                        if (_PendingEnumeration.IsMatch(code)) enumeratesPending = true;
                        if (_TryAssignCall.IsMatch(code) && !_MethodDeclaration.IsMatch(code)) assigns = true;
                    }

                    if (enumeratesPending && assigns) offenders.Add(file.Relative + " enumerates Pending missions and assigns them (a second dispatch loop)");
                }

                AssertEqual(0, offenders.Count, "dispatch outside AdmiralService (raise a dispatch wake instead):\n" + String.Join("\n", offenders));
                AssertTrue(_DispatchPendingDeclaration.IsMatch("        public async Task<bool> DispatchPendingMissionsAsync(CancellationToken token = default)"), "the scan recognizes a dispatch loop declaration");
                AssertTrue(_TryAssignCall.IsMatch("                    await TryAssignAsync(mission, vessel, token).ConfigureAwait(false);"), "the scan recognizes an assignment");
                AssertFalse(_TryAssignCall.IsMatch("                    await _Missions.TryAssignOwnerAsync(mission).ConfigureAwait(false);"), "other methods are not assignments");
                return Task.CompletedTask;
            }));

            cases.Add(Case("no_partial_classes", "No type is split across partial files, except the Avalonia windows the XAML compiler generates the other half of", () =>
            {
                List<string> offenders = new List<string>();
                foreach (SourceFile file in Sources(_AllCSharpProjects))
                {
                    bool xamlOwner = file.Relative.StartsWith("Armada.Harbor/", StringComparison.Ordinal)
                        && (file.Declares("App") || file.Declares("MainWindow") || file.Declares("AboutWindow"));
                    if (xamlOwner) continue;
                    for (int i = 0; i < file.Code.Length; i++)
                        if (_PartialDeclaration.IsMatch(file.Code[i])) offenders.Add(file.Location(i));
                }

                AssertEqual(0, offenders.Count, "partial type declarations (split the type into collaborating classes, one per file):\n" + String.Join("\n", offenders));
                AssertTrue(_PartialDeclaration.IsMatch("    public sealed partial class ArmadaClient"), "the scan recognizes a partial class");
                AssertTrue(_PartialDeclaration.IsMatch("    internal partial record Foo"), "the scan recognizes a partial record");
                AssertFalse(_PartialDeclaration.IsMatch("            // the partial class as it was"), "a comment is not a declaration");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.SourceGuards",
                displayName: "Source Guards",
                cases: cases);
        }

        #endregion

        #region Private-Members

        private static readonly string[] _ProductProjects = new[] { "Armada.Core", "Armada.Server", "Armada.Runtimes" };

        private static readonly string[] _AllCSharpProjects = new[]
        {
            "Armada.Benchmark", "Armada.Client", "Armada.Core", "Armada.Gate", "Armada.Harbor", "Armada.Helm", "Armada.LoadGenerator",
            "Armada.PerfSeed", "Armada.Proxy", "Armada.Publisher", "Armada.Runtimes", "Armada.Server", "Armada.Tui",
            "Test.Automated", "Test.Nunit", "Test.Shared", "Test.Smoke", "Test.Xunit"
        };

        private static readonly Regex _PartialDeclaration = new Regex(@"^\s*((public|internal|private|protected|sealed|static|abstract|readonly|unsafe|file)\s+)*partial\s+(class|record|struct|interface)\s+\w+", RegexOptions.Compiled);

        private static readonly Regex _LegacyMarker = new Regex(@"## Escalated to|ARMADA:ARCHITECT-HANDOFF|armada:fanout-complete|\bLegacy(EscalatedHeading|ArchitectHandoffMarker|FanOutCompleteMarker)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _DescriptionMatch = new Regex(@"\b[dD]escription\w*\??\.(Contains|IndexOf|LastIndexOf|StartsWith|EndsWith)\(|Regex\.\w+\([^;]*[dD]escription", RegexOptions.Compiled);
        private static readonly Regex _TitleLookup = new Regex(@"Title\s*==[^;]*\[(Worker|Judge|Architect)\]|\[(Worker|Judge|Architect)\]""\s*==", RegexOptions.Compiled);
        private static readonly Regex _MessageMatch = new Regex(@"Message\??\.(Contains|IndexOf|LastIndexOf|StartsWith|EndsWith)\(|Regex\.\w+\([^;]*\.Message\b", RegexOptions.Compiled);
        private static readonly Regex _FacadeForward = new Regex(@"^\s*=>\s*_Context\.\w+\.\w+\(", RegexOptions.Compiled);
        private static readonly Regex _BeginDispatchPassCall = new Regex(@"\bBeginDispatchPass\s*\(", RegexOptions.Compiled);
        private static readonly Regex _DispatchPendingDeclaration = new Regex(@"\bTask(<[^>]+>)?\s+Dispatch\w*Pending\w*Async\s*\(", RegexOptions.Compiled);
        private static readonly Regex _PendingEnumeration = new Regex(@"EnumerateByStatusAsync\(\s*MissionStatusEnum\.Pending\b", RegexOptions.Compiled);
        private static readonly Regex _TryAssignCall = new Regex(@"\bTryAssignAsync\s*\(", RegexOptions.Compiled);
        private static readonly Regex _MethodDeclaration = new Regex(@"^\s*(public|internal|private|protected)?\s*(static\s+)?(async\s+)?(Task|IDisposable|bool)\S*\s+\w+\s*\(", RegexOptions.Compiled);

        // Description reads that stay: the "Touch only <files>" scope line is the operator's own instruction in the brief,
        // not state Armada wrote, so reading it is not a control marker.
        private static readonly string[] _AllowedDescriptionMatches = new[]
        {
            "_ScopedFilesDirectiveRegex.Matches(description)"
        };

        // Matches that stay, each with the reason no typed signal exists. Keyed by the matched code, not the file, so
        // they survive the code moving between files.
        private static readonly string[] _AllowedMessageMatches = new[]
        {
            // A Harbor error frame carries only the Harbor's text; a typed reason needs a Harbor protocol change.
            "harborFailure.HarborMessage.Contains(\"not installed\"",
            // RuntimeProviderError.Message is the provider's parsed error body, not an exception; some providers report
            // an exhausted quota only there, after ErrorType, the reset time and Retry-After were checked.
            "error.Message.Contains(\"insufficient_quota\""
        };

        #endregion

        #region Private-Methods

        private static bool IsLegacyMarker(string code)
        {
            return _LegacyMarker.IsMatch(code);
        }

        private static bool IsDescriptionMatch(string code)
        {
            return _DescriptionMatch.IsMatch(code);
        }

        private static bool IsAllowedDescriptionMatch(string code)
        {
            return _AllowedDescriptionMatches.Any(allowed => code.Contains(allowed, StringComparison.Ordinal));
        }

        private static bool IsTitleLookup(string code)
        {
            return _TitleLookup.IsMatch(code);
        }

        private static bool IsMessageMatch(string code)
        {
            return _MessageMatch.IsMatch(code);
        }

        private static bool IsAllowedMessageMatch(string code)
        {
            return _AllowedMessageMatches.Any(allowed => code.Contains(allowed, StringComparison.Ordinal));
        }

        private static List<SourceFile> Sources(IEnumerable<string> projects)
        {
            string src = Path.Combine(ApiSurfaceFiles.FindRepositoryRoot(), "src");
            List<SourceFile> files = new List<SourceFile>();
            foreach (string project in projects)
                foreach (string path in Directory.EnumerateFiles(Path.Combine(src, project), "*.cs", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(src, path).Replace('\\', '/');
                    if (relative.Contains("/obj/") || relative.Contains("/bin/")) continue;
                    files.Add(new SourceFile(relative, File.ReadAllLines(path)));
                }
            return files;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.SourceGuards",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.Positive });
        }

        #endregion
    }
}
