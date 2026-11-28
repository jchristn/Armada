namespace Armada.Core.Services
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Text.Encodings.Web;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Text.Json.Serialization.Metadata;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// The one source of every stage's output contract: the Worker result, the Test Engineer, Linter, Usability
    /// Engineer and Product Manager reports, the Judge verdict, and the Architect plan. Prompt text, the plan's JSON
    /// example and field table, and the accepted verdict lines are generated here from the C# models and
    /// <see cref="JudgeVerdictEnum"/>, so the prompts and the parsers cannot drift apart. Persona templates carry no
    /// contract; the instructions append <see cref="For"/> once.
    /// </summary>
    public static class StageOutputContract
    {
        #region Public-Members

        /// <summary>
        /// Heading that opens every generated contract. A rendered persona prompt holds exactly one.
        /// </summary>
        public const string Heading = "## Output Contract";

        /// <summary>
        /// Prefix of a result line.
        /// </summary>
        public const string ResultMarker = "[ARMADA:RESULT]";

        /// <summary>
        /// Prefix of a Judge verdict line.
        /// </summary>
        public const string VerdictMarker = "[ARMADA:VERDICT]";

        /// <summary>
        /// The standalone line that ends successful non-Judge work.
        /// </summary>
        public const string ResultCompleteLine = ResultMarker + " COMPLETE";

        /// <summary>
        /// The Architect plan contract version the prompts ask for.
        /// </summary>
        public const int PlanContractVersion = 4;

        /// <summary>
        /// The sentence that ends a Worker-style contract. The rework brief uses it as its closing line.
        /// </summary>
        public const string CompletionSentence = _CompletionLead + " " + ResultCompleteLine;

        /// <summary>
        /// Verdicts a Judge may end with, in the order the prompt lists them (every value except None).
        /// </summary>
        public static IReadOnlyList<JudgeVerdictEnum> AcceptedVerdicts { get; } = Enum.GetValues(typeof(JudgeVerdictEnum))
            .Cast<JudgeVerdictEnum>()
            .Where(v => v != JudgeVerdictEnum.None)
            .ToList();

        /// <summary>
        /// Plan fields the Architect does not write, left out of the example and the field table: <c>dependsOn</c> (the
        /// pre-v4 name of <c>parent</c>, still read) and <c>workerEnvelope</c> (set by operators).
        /// </summary>
        public static IReadOnlyList<string> PlanFieldsNotPrompted { get; } = new List<string> { "missions[].dependsOn", "missions[].workerEnvelope" };

        #endregion

        #region Private-Members

        private const string _OutsideFence = "outside any code block";

        private const string FindingsInstruction = "List each defect under a ## Findings section, one numbered item each: Severity: blocking or non_blocking. File: <path>[:line]. Scenario: <inputs, violated requirement, expected and actual behavior>. Required fix: <correction>.";

        private const string _CompletionLead = "End successful work with this standalone line, " + _OutsideFence + ", followed by a brief plain-text summary:";

        private static readonly Dictionary<string, string> _PlanFieldMeanings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["contractVersion"] = "Always 4.",
            ["requirements"] = "Objective requirement IDs only (for example REQ-1), never \"REQ-1: text\". Keep every ID the objective or Product Manager assigned; otherwise assign one per independent requirement.",
            ["decisions"] = "Atomic resolved rules (compatibility, defaults versus overrides, failure handling). Split compound decisions.",
            ["missions"] = "The slices, in order. The last one is the integration slice with waitForOtherMissions true.",
            ["missions[].kind"] = "implementation, investigation or integration.",
            ["missions[].acceptanceCriteria"] = "Named checks. Every requirement ID and decision needs one in the slice that implements it.",
            ["missions[].acceptanceCriteria[].id"] = "Unique across the plan, for example AC-1.",
            ["missions[].acceptanceCriteria[].requirementIds"] = "IDs from requirements, matched exactly including case.",
            ["missions[].acceptanceCriteria[].decisionIndexes"] = "1-based plan decisions this check verifies, within the slice's decisionIndexes; [] for a general check.",
            ["missions[].acceptanceCriteria[].scenario"] = "The inputs or situation.",
            ["missions[].acceptanceCriteria[].expected"] = "The observable result.",
            ["missions[].acceptanceCriteria[].verification"] = "The named test or check that proves it.",
            ["missions[].acceptanceCriteria[].negativeCases"] = "Failure inputs and their expected results.",
            ["missions[].acceptanceCriteria[].negativeCasesNotApplicableReason"] = "Only when no negative case applies: why.",
            ["missions[].decisionIndexes"] = "1-based indexes of only the decisions this slice implements; [] if none.",
            ["missions[].validationCommands"] = "Exact commands that validate the slice.",
            ["missions[].title"] = "Short title, under 80 characters.",
            ["missions[].description"] = "Stands alone: Outcome; Components (verified paths, mark new); Approach; Scope exclusions; Invariants; Uncertainties.",
            ["missions[].parent"] = "1-based index of an earlier slice whose code this one needs, or null. A child starts after its parent finishes and branches from its result; siblings run in parallel.",
            ["missions[].ownedPaths"] = "Files or dir/ prefixes this slice may change. Slices that can run at the same time never own the same file.",
            ["missions[].readsPaths"] = "Context the slice needs but must not change.",
            ["missions[].branchStrategy"] = "Optional, needs parent: continue or fork.",
            ["missions[].waitForOtherMissions"] = "True only for a slice that waits for every other slice, such as the final integration."
        };

        // Example property order: what a reader needs first (title, description) before the checks.
        private static readonly List<string> _ExampleOrder = new List<string>
        {
            "contractVersion", "requirements", "decisions", "missions",
            "title", "description", "kind", "parent", "ownedPaths", "readsPaths", "branchStrategy", "waitForOtherMissions",
            "decisionIndexes", "validationCommands", "acceptanceCriteria",
            "id", "requirementIds", "scenario", "expected", "verification", "negativeCases", "negativeCasesNotApplicableReason"
        };

        private static readonly HashSet<string> _OmitWhenNull = new HashSet<string>(StringComparer.Ordinal)
        {
            "branchStrategy",
            "negativeCasesNotApplicableReason"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// The verdict token a Judge line carries for a verdict, generated from the enum name (NeedsRevision becomes
        /// NEEDS_REVISION).
        /// </summary>
        /// <param name="verdict">Verdict.</param>
        /// <returns>The token.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for <see cref="JudgeVerdictEnum.None"/> or an undefined value.</exception>
        public static string VerdictToken(JudgeVerdictEnum verdict)
        {
            if (verdict == JudgeVerdictEnum.None || !Enum.IsDefined(typeof(JudgeVerdictEnum), verdict))
                throw new ArgumentOutOfRangeException(nameof(verdict));
            string name = verdict.ToString();
            StringBuilder token = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && Char.IsUpper(name[i])) token.Append('_');
                token.Append(Char.ToUpperInvariant(name[i]));
            }
            return token.ToString();
        }

        /// <summary>
        /// The canonical standalone line for a verdict, for example <c>[ARMADA:VERDICT] NEEDS_REVISION</c>.
        /// </summary>
        /// <param name="verdict">Verdict.</param>
        /// <returns>The line.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for <see cref="JudgeVerdictEnum.None"/> or an undefined value.</exception>
        public static string VerdictLine(JudgeVerdictEnum verdict)
        {
            return VerdictMarker + " " + VerdictToken(verdict);
        }

        /// <summary>
        /// The report sections a persona's output must contain before its final line, in order. Empty for the Worker,
        /// the Architect and personas without a contract.
        /// </summary>
        /// <param name="persona">Persona name.</param>
        /// <returns>Section names without the leading hashes.</returns>
        public static IReadOnlyList<string> RequiredSections(string? persona)
        {
            switch (PersonaCatalog.NormalizeName(persona))
            {
                case PersonaCatalog.Judge:
                    List<string> judge = new List<string>(JudgeContract.RequiredLenses);
                    judge.Add("Verdict");
                    return judge;
                case PersonaCatalog.TestEngineer:
                    return new List<string> { "Coverage Added", "Negative Paths", "Residual Risks" };
                case PersonaCatalog.Linter:
                    return new List<string> { "Code Style", "Code Correctness", "Documentation", "Fixes Applied", "Residual Issues" };
                case PersonaCatalog.UsabilityEngineer:
                    return new List<string> { "Usability", "Consistency", "Edge Cases", "Residual Risks" };
                case PersonaCatalog.ProductManager:
                    return new List<string> { "Product Vision", "Use Cases", "Experience Requirements", "Validation", "Future Readiness" };
                default:
                    return new List<string>();
            }
        }

        /// <summary>
        /// The full output contract for a persona, opening with <see cref="Heading"/>, or an empty string for a persona
        /// without one (the Recorder and unknown personas).
        /// </summary>
        /// <param name="persona">Persona name.</param>
        /// <returns>Contract text.</returns>
        public static string For(string? persona)
        {
            string name = PersonaCatalog.NormalizeName(persona);
            switch (name)
            {
                case PersonaCatalog.Architect:
                    return ArchitectContract();
                case PersonaCatalog.Judge:
                    return JudgeVerdictContract();
                case PersonaCatalog.Worker:
                case PersonaCatalog.ProductManager:
                    return ResultContract(name, false);
                case PersonaCatalog.TestEngineer:
                case PersonaCatalog.Linter:
                case PersonaCatalog.UsabilityEngineer:
                    return ResultContract(name, true);
                default:
                    return String.Empty;
            }
        }

        /// <summary>
        /// Append a persona's contract to a rendered persona prompt.
        /// </summary>
        /// <param name="persona">Persona name.</param>
        /// <param name="personaPrompt">Rendered persona prompt.</param>
        /// <returns>The prompt followed by the contract, or the prompt unchanged when the persona has no contract.</returns>
        public static string AppendTo(string? persona, string? personaPrompt)
        {
            string prompt = personaPrompt ?? String.Empty;
            string contract = For(persona);
            if (String.IsNullOrEmpty(contract)) return prompt;
            if (prompt.Trim().Length == 0) return contract;
            return prompt.TrimEnd('\n', '\r', ' ') + "\n\n" + contract;
        }

        /// <summary>
        /// The one-line role summary for the launch prompt: who the captain is and how its output must end.
        /// </summary>
        /// <param name="persona">Persona name.</param>
        /// <returns>Summary text.</returns>
        public static string RoleSummary(string? persona)
        {
            string name = PersonaCatalog.NormalizeName(persona);
            switch (name)
            {
                case PersonaCatalog.Architect:
                    return "You are an Armada architect agent. Respond with exactly one fenced " + ArchitectPlanParser.FenceInfo + " JSON block (contractVersion " + PlanContractVersion + "). Do not emit " + ResultMarker + " or " + VerdictMarker + " lines.";
                case PersonaCatalog.Judge:
                    return "You are an Armada judge agent. Use " + SectionList(RequiredSections(name)) + " sections, add a ## " + JudgeContract.AffectedCaseSection
                        + " section with a File: or Scenario: line if you block, and end with exactly one standalone verdict line: "
                        + String.Join(", ", AcceptedVerdicts.Select(VerdictLine)) + ".";
                case PersonaCatalog.Worker:
                case PersonaCatalog.ProductManager:
                case PersonaCatalog.TestEngineer:
                case PersonaCatalog.Linter:
                case PersonaCatalog.UsabilityEngineer:
                    IReadOnlyList<string> sections = RequiredSections(name);
                    string include = sections.Count == 0 ? String.Empty : "Include " + SectionList(sections) + " sections, then end";
                    return "You are an Armada " + name.ToLowerInvariant() + " agent. " + (include.Length == 0 ? "End" : include)
                        + " with a standalone " + ResultCompleteLine + " line followed by a brief plain-text summary.";
                default:
                    return "You are an Armada captain executing a mission.";
            }
        }

        /// <summary>
        /// A complete, valid contract v4 plan, serialized from <see cref="ArchitectPlan"/>.
        /// </summary>
        /// <returns>Indented JSON.</returns>
        public static string ArchitectPlanExample()
        {
            ArchitectPlan plan = new ArchitectPlan
            {
                ContractVersion = PlanContractVersion,
                Requirements = new List<string> { "REQ-1" },
                Decisions = new List<string> { "An empty or whitespace name is rejected with status 400 and the message 'name is required'." },
                Missions = new List<ArchitectPlanMission>
                {
                    new ArchitectPlanMission
                    {
                        Title = "Reject empty widget names",
                        Description = "Outcome: creating a widget with an empty name fails with 400. Components: src/widgets/validator (new), src/widgets/routes. Approach: validate before saving. Scope exclusions: renaming. Invariants: valid names still save. Uncertainties: none.",
                        Kind = "implementation",
                        Parent = null,
                        OwnedPaths = new List<string> { "src/widgets/validator", "src/widgets/routes", "tests/widgets/" },
                        ReadsPaths = new List<string> { "src/widgets/model" },
                        WaitForOtherMissions = false,
                        DecisionIndexes = new List<int> { 1 },
                        ValidationCommands = new List<string> { "make test-widgets" },
                        AcceptanceCriteria = new List<ArchitectAcceptanceCriterion>
                        {
                            new ArchitectAcceptanceCriterion
                            {
                                Id = "AC-1",
                                RequirementIds = new List<string> { "REQ-1" },
                                DecisionIndexes = new List<int> { 1 },
                                Scenario = "POST a widget whose name is empty",
                                Expected = "400 with 'name is required'; nothing is saved",
                                Verification = "tests/widgets: rejects empty name",
                                NegativeCases = new List<string> { "a whitespace-only name returns 400", "a valid name still returns 201" }
                            }
                        }
                    },
                    new ArchitectPlanMission
                    {
                        Title = "Integrate and document name validation",
                        Description = "Outcome: the change is integrated and documented. Components: CHANGELOG.md. Approach: record the new 400 response. Scope exclusions: code changes. Invariants: none. Uncertainties: none.",
                        Kind = "integration",
                        Parent = null,
                        OwnedPaths = new List<string> { "CHANGELOG.md" },
                        ReadsPaths = new List<string>(),
                        WaitForOtherMissions = true,
                        DecisionIndexes = new List<int>(),
                        ValidationCommands = new List<string> { "make test" },
                        AcceptanceCriteria = new List<ArchitectAcceptanceCriterion>
                        {
                            new ArchitectAcceptanceCriterion
                            {
                                Id = "AC-2",
                                RequirementIds = new List<string> { "REQ-1" },
                                DecisionIndexes = new List<int>(),
                                Scenario = "Run the full test suite after integration",
                                Expected = "All tests pass and the changelog lists the 400 response",
                                Verification = "make test",
                                NegativeCasesNotApplicableReason = "Integration only; the failure paths are covered by AC-1."
                            }
                        }
                    }
                }
            };

            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { PromptedFieldsOnly } }
            };
            return JsonSerializer.Serialize(plan, options).Replace("\r\n", "\n");
        }

        /// <summary>
        /// Every plan field the prompt documents, as a JSON path (for example <c>missions[].ownedPaths</c>), read from the
        /// models' <see cref="JsonPropertyNameAttribute"/>s.
        /// </summary>
        /// <returns>Field paths in declaration order.</returns>
        public static IReadOnlyList<string> PlanFieldPaths()
        {
            List<string> paths = new List<string>();
            AddFieldPaths(typeof(ArchitectPlan), String.Empty, paths);
            return paths;
        }

        /// <summary>
        /// What a documented plan field means, for the field table and the <c>armada_submit_plan</c> schema.
        /// </summary>
        /// <param name="path">Field path as <see cref="PlanFieldPaths"/> lists it.</param>
        /// <returns>The meaning, or null for a field the prompt does not document.</returns>
        public static string? PlanFieldMeaning(string path)
        {
            if (String.IsNullOrEmpty(path)) return null;
            return _PlanFieldMeanings.TryGetValue(path, out string? meaning) ? meaning : null;
        }

        /// <summary>
        /// The number of generated contracts in a text (occurrences of <see cref="Heading"/> at the start of a line).
        /// </summary>
        /// <param name="text">Text to scan.</param>
        /// <returns>The count.</returns>
        public static int CountContracts(string? text)
        {
            if (String.IsNullOrEmpty(text)) return 0;
            return text.Replace("\r", String.Empty).Split('\n').Count(line => String.Equals(line.TrimEnd(), Heading, StringComparison.Ordinal));
        }

        #endregion

        #region Private-Methods

        private static string ResultContract(string persona, bool verifier)
        {
            StringBuilder text = new StringBuilder();
            text.Append(Heading).Append('\n');
            text.Append(SubmitHint(StageSubmissionKindEnum.Result)).Append('\n');
            IReadOnlyList<string> sections = RequiredSections(persona);
            if (sections.Count > 0)
            {
                text.Append("Before the final line, include these short sections:\n");
                foreach (string section in sections) text.Append("- ## ").Append(section).Append('\n');
            }
            if (verifier)
            {
                text.Append("If you find a defect in product code your role must not change, do not report COMPLETE and do not work around it: add a ## ")
                    .Append(JudgeContract.AffectedCaseSection)
                    .Append(" section with a File: <path>[:line] or Scenario: <inputs and wrong result> line, the violated requirement, expected and actual behavior, and the required correction, then end with this standalone line instead:\n")
                    .Append(VerifierContract.BlockedMarker).Append('\n');
                text.Append(FindingsInstruction).Append('\n');
            }
            text.Append(_CompletionLead).Append('\n');
            text.Append(ResultCompleteLine).Append('\n');
            return text.ToString();
        }

        private static string JudgeVerdictContract()
        {
            StringBuilder text = new StringBuilder();
            text.Append(Heading).Append('\n');
            text.Append(SubmitHint(StageSubmissionKindEnum.Verdict)).Append('\n');
            text.Append("Use these sections, even when you have no findings:\n");
            foreach (string section in RequiredSections(PersonaCatalog.Judge)) text.Append("- ## ").Append(section).Append('\n');
            text.Append("A ").Append(VerdictToken(JudgeVerdictEnum.Pass))
                .Append(" must name the checks you ran (for example the build or test command) or at least one file you inspected.\n");
            text.Append("To block (").Append(VerdictToken(JudgeVerdictEnum.Fail)).Append(" or ").Append(VerdictToken(JudgeVerdictEnum.NeedsRevision))
                .Append("), add a ## ").Append(JudgeContract.AffectedCaseSection).Append(" section with a ")
                .Append(JudgeContract.AffectedCaseFileField).Append(": <path>[:line] or ")
                .Append(JudgeContract.AffectedCaseScenarioField).Append(": <inputs and wrong result> line, the violated requirement, expected and actual behavior, and the required correction. Without one you have no grounds to block.\n");
            text.Append(FindingsInstruction).Append('\n');
            text.Append("If an acceptance criterion cannot be satisfied as written (it contradicts another requirement or a dependency's verified behavior), do not request rework: add a ## ")
                .Append(JudgeContract.SpecConflictSection).Append(" section quoting the criterion and the evidence, and use ")
                .Append(VerdictToken(JudgeVerdictEnum.SpecConflict)).Append("; a person decides.\n");
            text.Append("End with exactly one of these standalone lines, ").Append(_OutsideFence).Append(":\n");
            foreach (JudgeVerdictEnum verdict in AcceptedVerdicts) text.Append(VerdictLine(verdict)).Append('\n');
            return text.ToString();
        }

        private static string ArchitectContract()
        {
            StringBuilder text = new StringBuilder();
            text.Append(Heading).Append('\n');
            text.Append(SubmitHint(StageSubmissionKindEnum.Plan)).Append('\n');
            text.Append("Respond with exactly one fenced ").Append(ArchitectPlanParser.FenceInfo).Append(" JSON block, contract version ")
                .Append(PlanContractVersion).Append(". A complete example:\n");
            text.Append("```").Append(ArchitectPlanParser.FenceInfo).Append('\n');
            text.Append(ArchitectPlanExample()).Append('\n');
            text.Append("```\n");
            text.Append("Fields:\n");
            text.Append("| Field | Type | Meaning |\n");
            text.Append("|---|---|---|\n");
            foreach (string path in PlanFieldPaths())
            {
                text.Append("| ").Append(path).Append(" | ").Append(FieldType(path)).Append(" | ").Append(_PlanFieldMeanings[path]).Append(" |\n");
            }
            text.Append("Plans that fail validation are returned to you with the errors before any Worker starts. Do not emit ")
                .Append(ResultMarker).Append(" or ").Append(VerdictMarker).Append(" lines.\n");
            return text.ToString();
        }

        private static string SubmitHint(StageSubmissionKindEnum kind)
        {
            return "If your tools include " + StageSubmissionTools.ToolFor(kind) + ", submit through it: it checks the submission at once and returns any errors to fix, and the accepted submission is your result. Otherwise write the output below.";
        }

        private static string SectionList(IReadOnlyList<string> sections)
        {
            List<string> headed = sections.Select(s => "## " + s).ToList();
            if (headed.Count <= 1) return String.Join(String.Empty, headed);
            return String.Join(", ", headed.Take(headed.Count - 1)) + " and " + headed[headed.Count - 1];
        }

        private static void AddFieldPaths(Type type, string prefix, List<string> paths)
        {
            IEnumerable<PropertyInfo> properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() == null && p.GetCustomAttribute<JsonPropertyNameAttribute>() != null)
                .OrderBy(p => OrderOf(type, p.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name));
            foreach (PropertyInfo property in properties)
            {
                JsonPropertyNameAttribute jsonName = property.GetCustomAttribute<JsonPropertyNameAttribute>()!;
                string path = prefix + jsonName.Name;
                if (PlanFieldsNotPrompted.Contains(path)) continue;
                paths.Add(path);
                Type? element = ElementType(property.PropertyType);
                if (element != null && element.Namespace == typeof(ArchitectPlan).Namespace)
                    AddFieldPaths(element, path + "[].", paths);
            }
        }

        private static string FieldType(string path)
        {
            Type current = typeof(ArchitectPlan);
            PropertyInfo? found = null;
            foreach (string segment in path.Split('.'))
            {
                string name = segment.EndsWith("[]", StringComparison.Ordinal) ? segment.Substring(0, segment.Length - 2) : segment;
                found = current.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .First(p => String.Equals(p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name, name, StringComparison.Ordinal));
                current = ElementType(found.PropertyType) ?? found.PropertyType;
            }

            Type type = found!.PropertyType;
            Type? item = ElementType(type);
            if (item != null) return item == typeof(string) ? "string list" : item == typeof(int) ? "integer list" : "object list";
            Type core = Nullable.GetUnderlyingType(type) ?? type;
            if (core == typeof(int)) return Nullable.GetUnderlyingType(type) != null && path != "contractVersion" ? "integer or null" : "integer";
            if (core == typeof(bool)) return "boolean";
            return "string";
        }

        private static Type? ElementType(Type type)
        {
            if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type)) return null;
            return type.IsGenericType ? type.GetGenericArguments()[0] : null;
        }

        private static int OrderOf(Type type, string jsonName)
        {
            // A criterion lists its decision indexes right after its requirement IDs.
            if (type == typeof(ArchitectAcceptanceCriterion) && jsonName == "decisionIndexes") jsonName = "requirementIds";
            int order = _ExampleOrder.IndexOf(jsonName);
            return order < 0 ? _ExampleOrder.Count : order;
        }

        private static void PromptedFieldsOnly(JsonTypeInfo info)
        {
            if (info.Kind != JsonTypeInfoKind.Object) return;
            for (int i = info.Properties.Count - 1; i >= 0; i--)
            {
                JsonPropertyInfo property = info.Properties[i];
                bool notPrompted = PlanFieldsNotPrompted.Any(p => p.EndsWith("]." + property.Name, StringComparison.Ordinal) || p == property.Name);
                if (notPrompted && info.Type == typeof(ArchitectPlanMission))
                {
                    info.Properties.RemoveAt(i);
                    continue;
                }
                if (_OmitWhenNull.Contains(property.Name))
                    property.ShouldSerialize = (owner, value) => value != null;
                property.Order = OrderOf(info.Type, property.Name);
            }
        }

        #endregion
    }
}
