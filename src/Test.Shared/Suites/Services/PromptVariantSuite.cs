namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Persona prompt variants (Current, Lean, Compact; docs/MODEL_HETEROGENEITY.md Phase 4): the variant templates in
    /// the template store, rendering per persona (Worker, Test Engineer, Judge, Architect) with acceptance criteria and
    /// project rules kept verbatim, the Lean contract repeated at the end, Current output unchanged byte for byte, and
    /// the selection order (voyage override, then captain option, then Current).
    /// </summary>
    public sealed class PromptVariantSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.PromptVariants";

        private const string AcceptanceCriteria =
            "Acceptance criteria:\n" +
            "- AC-1: GET /api/v1/widgets returns 404 for an unknown id.\n" +
            "- AC-2: an empty name is rejected with 400 and the message `name is required`.";

        private const string ProjectContext = "PROJECT RULE: never use var; private fields are _PascalCase.";

        private const string StyleGuide = "STYLE RULE: one class per file, XML docs on public members.";

        private const string ExistingRules = "# Repo rules\n\nREPO RULE: run `dotnet build src/Widgets.sln` before committing.\n";

        private static readonly PersonaCase[] _Personas = new[]
        {
            new PersonaCase("Worker", "persona.worker", StageOutputContract.For("Worker")),
            new PersonaCase("Test Engineer", "persona.test_engineer", StageOutputContract.For("Test Engineer")),
            new PersonaCase("Judge", "persona.judge", StageOutputContract.For("Judge")),
            new PersonaCase("Architect", "persona.architect", StageOutputContract.For("Architect"))
        };

        private static readonly PromptVariantEnum[] _Variants = new[] { PromptVariantEnum.Current, PromptVariantEnum.Lean, PromptVariantEnum.Compact };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Prompt Variants suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("variant_templates_are_listed_and_seeded", "Lean and Compact templates for every persona are built in, listed and seeded unchanged", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    PromptTemplateService templates = new PromptTemplateService(testDb.Driver, CreateLogging());
                    List<string> names = templates.GetEmbeddedDefaultNames();
                    await templates.SeedDefaultsAsync().ConfigureAwait(false);
                    List<PromptTemplate> listed = await templates.ListAsync("persona").ConfigureAwait(false);

                    foreach (PersonaCase persona in _Personas)
                    {
                        AssertEqual(persona.TemplateName, MissionPromptBuilder.GetPersonaVariantTemplateName(persona.Name, PromptVariantEnum.Current), persona.Name + " Current is the persona's own template");
                        foreach (PromptVariantEnum variant in new[] { PromptVariantEnum.Lean, PromptVariantEnum.Compact })
                        {
                            string name = MissionPromptBuilder.GetPersonaVariantTemplateName(persona.Name, variant);
                            AssertEqual(persona.TemplateName + "." + variant.ToString().ToLowerInvariant(), name, "variant template name");
                            AssertEqual(name, MissionPromptBuilder.GetPersonaVariantTemplateName(persona.TemplateName, variant), "a persona template name maps to the same variant name");
                            AssertTrue(names.Contains(name), name + " is a built-in template");
                            AssertTrue(listed.Any(t => t.Name == name && t.IsBuiltIn), name + " is listed under the persona category");
                            PromptTemplate? seeded = await testDb.Driver.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false);
                            AssertNotNull(seeded, name + " is seeded");
                            AssertEqual(templates.GetEmbeddedDefault(name), seeded!.Content, name + " is seeded without appended guidance");
                        }
                    }
                }
            }));

            cases.Add(CaseAsync("current_variant_renders_byte_for_byte_unchanged", "Current renders exactly the persona's own template, and the generated instructions do not change", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    LoggingModule logging = CreateLogging();
                    MissionService service = CreateMissionService(logging, testDb.Driver, out IPromptTemplateService templates);
                    foreach (PersonaCase persona in _Personas)
                    {
                        Fixture fixture = CreateFixture(persona.Name);
                        Dictionary<string, string> templateParams = MissionPromptBuilder.BuildTemplateParams(fixture.Mission, fixture.Vessel, fixture.Captain);
                        string direct = StageOutputContract.AppendTo(persona.Name, await templates.RenderAsync(persona.TemplateName, templateParams).ConfigureAwait(false));
                        string legacy = await MissionPromptBuilder.ResolvePersonaPromptAsync(persona.Name, null, templateParams, templates).ConfigureAwait(false);
                        string current = await MissionPromptBuilder.ResolvePersonaPromptAsync(persona.Name, null, PromptVariantEnum.Current, templateParams, templates).ConfigureAwait(false);
                        AssertEqual(direct, legacy, persona.Name + " legacy overload renders the persona template and its contract");
                        AssertEqual(direct, current, persona.Name + " Current renders the persona template and its contract byte for byte");

                        string baseline = await GenerateAsync(service, fixture, null).ConfigureAwait(false);
                        string explicitCurrent = await GenerateAsync(service, fixture, "{\"promptVariant\":\"Current\"}").ConfigureAwait(false);
                        string otherOptions = await GenerateAsync(service, fixture, "{\"autoApprove\":false}").ConfigureAwait(false);
                        string unreadable = await GenerateAsync(service, fixture, "{\"promptVariant\":\"Huge\"}").ConfigureAwait(false);
                        string malformed = await GenerateAsync(service, fixture, "{not json").ConfigureAwait(false);
                        // RS-6.11: the instructions carry the persona prompt without its contract and close with the contract.
                        string directBody = await templates.RenderAsync(persona.TemplateName, templateParams).ConfigureAwait(false);
                        AssertContains(directBody, baseline, persona.Name + " instructions carry the Current persona prompt");
                        AssertTrue(baseline.TrimEnd().EndsWith(persona.Contract.TrimEnd(), StringComparison.Ordinal), persona.Name + " instructions close with the contract");
                        AssertEqual(baseline, explicitCurrent, persona.Name + " explicit Current");
                        AssertEqual(baseline, otherOptions, persona.Name + " options without a variant");
                        AssertEqual(baseline, unreadable, persona.Name + " an unknown variant name falls back to Current");
                        AssertEqual(baseline, malformed, persona.Name + " malformed options fall back to Current");
                    }
                }
            }));

            cases.Add(CaseAsync("variants_keep_acceptance_criteria_and_project_rules", "Every variant of every persona keeps the acceptance criteria and project rules verbatim and changes only the persona prompt", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MissionService service = CreateMissionService(CreateLogging(), testDb.Driver, out IPromptTemplateService templates);
                    foreach (PersonaCase persona in _Personas)
                    {
                        Fixture fixture = CreateFixture(persona.Name);
                        Dictionary<string, string> templateParams = MissionPromptBuilder.BuildTemplateParams(fixture.Mission, fixture.Vessel, fixture.Captain);
                        string currentPrompt = await MissionPromptBuilder.ResolvePersonaPromptAsync(persona.Name, null, PromptVariantEnum.Current, templateParams, templates).ConfigureAwait(false);
                        string currentBody = await BodyAsync(persona.Name, PromptVariantEnum.Current, templateParams, templates).ConfigureAwait(false);
                        string currentDoc = await GenerateAsync(service, fixture, null).ConfigureAwait(false);

                        foreach (PromptVariantEnum variant in _Variants)
                        {
                            string label = persona.Name + " " + variant;
                            string doc = await GenerateAsync(service, fixture, "{\"promptVariant\":\"" + variant + "\"}").ConfigureAwait(false);
                            string prompt = await MissionPromptBuilder.ResolvePersonaPromptAsync(persona.Name, null, variant, templateParams, templates).ConfigureAwait(false);

                            AssertContains(AcceptanceCriteria, doc, label + " acceptance criteria");
                            AssertContains(ProjectContext, doc, label + " project context");
                            AssertContains(StyleGuide, doc, label + " style guide");
                            AssertContains("REPO RULE: run `dotnet build src/Widgets.sln` before committing.", doc, label + " repository rules");
                            string body = await BodyAsync(persona.Name, variant, templateParams, templates).ConfigureAwait(false);
                            AssertContains(body, doc, label + " persona prompt");
                            AssertEqual(1, StageOutputContract.CountContracts(doc), label + " instructions state exactly one output contract");
                            AssertContains(persona.Contract, doc, label + " instructions carry the generated contract");
                            AssertEqual(currentDoc.Replace(currentBody, "<PERSONA>"), doc.Replace(body, "<PERSONA>"), label + " differs from Current only in the persona prompt");

                            if (variant != PromptVariantEnum.Current)
                            {
                                AssertEqual(StageOutputContract.AppendTo(persona.Name, await templates.RenderAsync(MissionPromptBuilder.GetPersonaVariantTemplateName(persona.Name, variant), templateParams).ConfigureAwait(false)), prompt, label + " renders its variant template and the contract");
                                AssertFalse(String.Equals(prompt, currentPrompt, StringComparison.Ordinal), label + " is not the Current prompt");
                            }
                        }
                    }
                }
            }));

            cases.Add(CaseAsync("every_persona_instructions_state_one_contract", "The generated instructions of every persona with a contract state it exactly once; the Recorder states none", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MissionService service = CreateMissionService(CreateLogging(), testDb.Driver, out IPromptTemplateService templates);
                    foreach (string persona in new[] { PersonaCatalog.Worker, PersonaCatalog.TestEngineer, PersonaCatalog.Judge, PersonaCatalog.Architect, PersonaCatalog.Linter, PersonaCatalog.ProductManager, PersonaCatalog.UsabilityEngineer, PersonaCatalog.Recorder })
                    {
                        string doc = await GenerateAsync(service, CreateFixture(persona), null).ConfigureAwait(false);
                        string contract = StageOutputContract.For(persona);
                        AssertEqual(contract.Length == 0 ? 0 : 1, StageOutputContract.CountContracts(doc), persona + " contract count");
                        if (contract.Length > 0) AssertContains(contract, doc, persona + " carries the generated contract");
                        AssertFalse(doc.Contains("`[ARMADA:RESULT]", StringComparison.Ordinal) || doc.Contains("`[ARMADA:VERDICT]", StringComparison.Ordinal), persona + " shows no result or verdict marker in backticks");
                        AssertFalse(doc.Contains("[ARMADA:MISSION]", StringComparison.Ordinal), persona + " shows only the versioned plan contract");
                    }
                }
            }));

            cases.Add(CaseAsync("every_launch_kind_states_one_contract", "RS-6.11: a first run, a rework round (current and pre-1.5.0 sections) and a rescue-shaped brief each state the contract once, at the end of the file", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MissionService service = CreateMissionService(CreateLogging(), testDb.Driver, out IPromptTemplateService templates);
                    string contract = StageOutputContract.For(PersonaCatalog.Worker);
                    string task = "Return 404 for unknown widgets.\n\n" + AcceptanceCriteria;
                    Dictionary<string, string> kinds = new Dictionary<string, string>
                    {
                        ["first run"] = task,
                        ["rework"] = task + ReworkPromptBuilder.BuildSection(1, PersonaCatalog.Judge),
                        ["pre-1.5.0 rework"] = task + ReworkPromptBuilder.BuildSection(1, PersonaCatalog.Judge) + "\n### Output contract\n" + ReworkPromptBuilder.OutputContract + "\n",
                        ["rescue"] = "This is an autonomous RESCUE mission.\n\n## Original Mission\n" + task + "\n\nClose with a brief summary of the fix, followed by the completion line in the Output Contract section at the end of these instructions."
                    };
                    foreach (KeyValuePair<string, string> kind in kinds)
                    {
                        Fixture fixture = CreateFixture(PersonaCatalog.Worker);
                        fixture.Mission.Description = kind.Value;
                        string doc = await GenerateAsync(service, fixture, null).ConfigureAwait(false);
                        AssertEqual(1, StageOutputContract.CountContracts(doc), kind.Key + ": one contract heading");
                        AssertEqual(1, doc.Replace("\r", "").Split('\n').Count(l => l.Trim() == StageOutputContract.ResultCompleteLine), kind.Key + ": the completion line is stated once");
                        AssertTrue(doc.TrimEnd().EndsWith(contract.TrimEnd(), StringComparison.Ordinal), kind.Key + ": the contract closes the file");
                        AssertContains(AcceptanceCriteria, doc, kind.Key + ": acceptance criteria kept");
                    }
                }
            }));

            cases.Add(CaseAsync("small_context_trims_in_order_and_keeps_contract", "RS-6.11: an API captain with a small served context gets the papercuts trimmed first; the description and contract are never trimmed; an unknown context trims nothing", TestTags.Reliability, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MissionService service = CreateMissionService(CreateLogging(), testDb.Driver, out IPromptTemplateService templates);
                    Fixture unknown = CreateFixture(PersonaCatalog.Worker);
                    string full = await GenerateAsync(service, unknown, null).ConfigureAwait(false);
                    AssertContains("## Papercuts", full, "an unknown context trims nothing");

                    ModelEndpoint endpoint = new ModelEndpoint();
                    endpoint.Name = "small-context";
                    endpoint.Model = "small";
                    endpoint.InferenceOptions = new InferenceOptions { ContextLength = 4096 };
                    endpoint = await testDb.Driver.ModelEndpoints.CreateAsync(endpoint).ConfigureAwait(false);
                    Fixture small = CreateFixture(PersonaCatalog.Worker);
                    small.Captain.Runtime = AgentRuntimeEnum.ApiEndpoint;
                    small.Captain.ModelEndpointId = endpoint.Id;
                    string trimmed = await GenerateAsync(service, small, null).ConfigureAwait(false);
                    AssertFalse(trimmed.Contains("## Papercuts", StringComparison.Ordinal), "papercuts trimmed first");
                    AssertContains("The papercut directive was left out to fit this model's context budget.", trimmed, "a note replaces it");
                    AssertContains(AcceptanceCriteria, trimmed, "acceptance criteria never trimmed");
                    AssertEqual(1, StageOutputContract.CountContracts(trimmed), "the contract is kept once");
                    AssertTrue(trimmed.Length < full.Length, "the file is smaller");
                    if (!trimmed.Contains(ProjectContext, StringComparison.Ordinal))
                        AssertFalse(trimmed.Contains("## Papercuts", StringComparison.Ordinal), "project context goes only after the papercuts");
                }
            }));

            cases.Add(CaseAsync("every_variant_ends_with_the_one_contract_and_compact_is_shorter", "Every variant ends with the one generated output contract, stated once; Lean puts one demand on each line; Compact is the shortest tier", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    PromptTemplateService templates = new PromptTemplateService(testDb.Driver, CreateLogging());
                    foreach (PersonaCase persona in _Personas)
                    {
                        Fixture fixture = CreateFixture(persona.Name);
                        Dictionary<string, string> templateParams = MissionPromptBuilder.BuildTemplateParams(fixture.Mission, fixture.Vessel, fixture.Captain);
                        string current = await MissionPromptBuilder.ResolvePersonaPromptAsync(persona.Name, null, PromptVariantEnum.Current, templateParams, templates).ConfigureAwait(false);
                        string lean = await MissionPromptBuilder.ResolvePersonaPromptAsync(persona.Name, null, PromptVariantEnum.Lean, templateParams, templates).ConfigureAwait(false);
                        string compact = await MissionPromptBuilder.ResolvePersonaPromptAsync(persona.Name, null, PromptVariantEnum.Compact, templateParams, templates).ConfigureAwait(false);

                        foreach (string prompt in new[] { current, lean, compact })
                        {
                            AssertTrue(prompt.EndsWith(persona.Contract, StringComparison.Ordinal), persona.Name + " ends with the generated contract");
                            AssertEqual(1, StageOutputContract.CountContracts(prompt), persona.Name + " states the contract once");
                        }
                        AssertFalse(Regex.IsMatch(lean, "\\b(MUST|CRITICAL|IMPORTANT|NEVER|ALWAYS)\\b"), persona.Name + " Lean has no emphasis wording");
                        string leanTemplate = templates.GetEmbeddedDefault(MissionPromptBuilder.GetPersonaVariantTemplateName(persona.Name, PromptVariantEnum.Lean))!;
                        foreach (string line in leanTemplate.Split('\n'))
                            AssertFalse(Regex.IsMatch(line, "\\.\\s+[A-Z]"), persona.Name + " Lean puts one demand on each line: " + line);

                        AssertTrue(compact.Length < current.Length, persona.Name + " Compact (" + compact.Length + ") is shorter than Current (" + current.Length + ")");
                        AssertTrue(compact.Length < lean.Length, persona.Name + " Compact (" + compact.Length + ") is shorter than Lean (" + lean.Length + ")");
                    }
                }
            }));

            cases.Add(CaseAsync("variants_keep_the_handoff_placeholders", "Test Engineer and Judge variants keep the prior stage evidence and diff placeholders", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    PromptTemplateService templates = new PromptTemplateService(testDb.Driver, CreateLogging());
                    foreach (string templateName in new[] { "persona.test_engineer", "persona.judge" })
                    {
                        foreach (PromptVariantEnum variant in _Variants)
                        {
                            string content = templates.GetEmbeddedDefault(MissionPromptBuilder.GetPersonaVariantTemplateName(templateName, variant))!;
                            AssertContains("{PreviousStageOutput}", content, templateName + " " + variant);
                            AssertContains("{Diff}", content, templateName + " " + variant);
                        }
                    }
                }
            }));

            cases.Add(CaseAsync("explicit_templates_beat_the_variant_and_missing_variants_fall_back", "A project-profile template or a persona's own template beats the variant; a persona without a variant renders Current", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    PromptTemplateService templates = new PromptTemplateService(testDb.Driver, CreateLogging());
                    await testDb.Driver.PromptTemplates.CreateAsync(new PromptTemplate("custom.worker", "CUSTOM WORKER PROMPT") { Category = "persona" }).ConfigureAwait(false);
                    await testDb.Driver.PromptTemplates.CreateAsync(new PromptTemplate("profile.worker", "PROFILE WORKER PROMPT") { Category = "persona" }).ConfigureAwait(false);
                    Dictionary<string, string> templateParams = new Dictionary<string, string>();

                    string personaOwn = await MissionPromptBuilder.ResolvePersonaPromptAsync("Worker", "custom.worker", PromptVariantEnum.Lean, templateParams, templates).ConfigureAwait(false);
                    AssertEqual(StageOutputContract.AppendTo("Worker", "CUSTOM WORKER PROMPT"), personaOwn, "a persona's own (non-default) template beats the variant");

                    PersonaOverride profile = new PersonaOverride { PersonaName = "Worker", PromptTemplateName = "profile.worker", AdditionalInstructions = "PROFILE EXTRA" };
                    string profiled = await MissionPromptBuilder.ResolvePersonaPromptAsync("Worker", null, PromptVariantEnum.Compact, templateParams, templates, profile).ConfigureAwait(false);
                    AssertEqual(StageOutputContract.AppendTo("Worker", "PROFILE WORKER PROMPT\n\nPROFILE EXTRA"), profiled, "a project-profile template beats the variant");

                    PersonaOverride extraOnly = new PersonaOverride { PersonaName = "Worker", AdditionalInstructions = "PROFILE EXTRA" };
                    string leanWithExtra = await MissionPromptBuilder.ResolvePersonaPromptAsync("Worker", "persona.worker", PromptVariantEnum.Lean, templateParams, templates, extraOnly).ConfigureAwait(false);
                    AssertEqual(StageOutputContract.AppendTo("Worker", templates.GetEmbeddedDefault("persona.worker.lean") + "\n\nPROFILE EXTRA"), leanWithExtra, "the default template name takes the variant; profile instructions still append");

                    string linterLean = await MissionPromptBuilder.ResolvePersonaPromptAsync("Linter", null, PromptVariantEnum.Lean, templateParams, templates).ConfigureAwait(false);
                    string linterCurrent = await MissionPromptBuilder.ResolvePersonaPromptAsync("Linter", null, PromptVariantEnum.Current, templateParams, templates).ConfigureAwait(false);
                    AssertEqual(linterCurrent, linterLean, "a persona without a Lean template renders its Current template");

                    string noStore = await MissionPromptBuilder.ResolvePersonaPromptAsync("Worker", null, PromptVariantEnum.Lean, templateParams, null).ConfigureAwait(false);
                    string noStoreCurrent = await MissionPromptBuilder.ResolvePersonaPromptAsync("Worker", null, PromptVariantEnum.Current, templateParams, null).ConfigureAwait(false);
                    AssertEqual(noStoreCurrent, noStore, "without a template store every variant uses the built-in role summary");
                }
            }));

            cases.Add(Case("selection_order_voyage_then_captain_then_current", "Variant selection: voyage override (exact persona, then wildcard), then the captain option, then Current", TestTags.Positive, () =>
            {
                Captain plain = new Captain("plain");
                Captain lean = new Captain("lean") { RuntimeOptionsJson = "{\"promptVariant\":\"Lean\",\"autoApprove\":false}" };
                Captain lowerCase = new Captain("lower") { RuntimeOptionsJson = "{\"promptVariant\":\"compact\"}" };

                AssertEqual(PromptVariantEnum.Current, PromptVariantResolver.Resolve("Worker", null, null), "nothing set: Current");
                AssertEqual(PromptVariantEnum.Current, PromptVariantResolver.Resolve("Worker", new List<CaptainAssignmentOverride>(), plain), "captain without options: Current");
                AssertEqual(PromptVariantEnum.Lean, PromptVariantResolver.Resolve("Worker", null, lean), "captain option");
                AssertEqual(PromptVariantEnum.Compact, PromptVariantResolver.Resolve("Judge", null, lowerCase), "captain option is case-insensitive");

                List<CaptainAssignmentOverride> wildcard = new List<CaptainAssignmentOverride>
                {
                    new CaptainAssignmentOverride("*", null, null) { PromptVariant = PromptVariantEnum.Compact }
                };
                AssertEqual(PromptVariantEnum.Compact, PromptVariantResolver.Resolve("Worker", wildcard, lean), "a voyage override beats the captain option");

                List<CaptainAssignmentOverride> mixed = new List<CaptainAssignmentOverride>
                {
                    new CaptainAssignmentOverride("*", null, null) { PromptVariant = PromptVariantEnum.Compact },
                    new CaptainAssignmentOverride("Worker", null, null) { PromptVariant = PromptVariantEnum.Lean },
                    new CaptainAssignmentOverride("Judge", "cpt_x", null)
                };
                AssertEqual(PromptVariantEnum.Lean, PromptVariantResolver.Resolve("Worker", mixed, plain), "an exact persona override beats the wildcard");
                AssertEqual(PromptVariantEnum.Lean, PromptVariantResolver.Resolve(null, mixed, plain), "a mission without a persona is a Worker");
                AssertEqual(PromptVariantEnum.Compact, PromptVariantResolver.Resolve("Judge", mixed, plain), "an exact override without a variant does not count; the wildcard applies");
                AssertEqual(PromptVariantEnum.Compact, PromptVariantResolver.Resolve("Test Engineer", mixed, plain), "other personas take the wildcard");

                List<CaptainAssignmentOverride> noVariant = new List<CaptainAssignmentOverride> { new CaptainAssignmentOverride("*", "cpt_x", null) { StrictCaptain = true } };
                AssertEqual(PromptVariantEnum.Lean, PromptVariantResolver.Resolve("Worker", noVariant, lean), "a voyage override without a variant leaves the captain option");
                AssertEqual(PromptVariantEnum.Current, PromptVariantResolver.Resolve("Worker", noVariant, plain), "a voyage override without a variant and no captain option: Current");

                List<CaptainAssignmentOverride> explicitCurrent = new List<CaptainAssignmentOverride> { new CaptainAssignmentOverride("*", null, null) { PromptVariant = PromptVariantEnum.Current } };
                AssertEqual(PromptVariantEnum.Current, PromptVariantResolver.Resolve("Worker", explicitCurrent, lean), "an explicit Current voyage override beats the captain option");
            }));

            cases.Add(Case("minimal_and_malformed_settings_resolve_to_current", "Minimally deserialized overrides and malformed captain options resolve safely", TestTags.Negative, () =>
            {
                AssertNull(MissionService.DeserializeCaptainOverrides("[{\"Persona\":\"*\",\"CaptainId\":\"cpt_x\"}]").Single().PromptVariant, "an override without the field has no variant");
                AssertEqual((PromptVariantEnum?)PromptVariantEnum.Lean, MissionService.DeserializeCaptainOverrides("[{\"Persona\":\"*\",\"PromptVariant\":\"Lean\"}]").Single().PromptVariant, "string variant");
                AssertEqual(0, MissionService.DeserializeCaptainOverrides("[{\"Persona\":\"*\",\"PromptVariant\":\"Huge\"}]").Count, "an unknown variant makes the overrides unreadable (empty), as for any malformed override");

                List<CaptainAssignmentOverride> roundTrip = MissionService.DeserializeCaptainOverrides(MissionService.SerializeCaptainOverrides(new List<CaptainAssignmentOverride>
                {
                    new CaptainAssignmentOverride("Judge", "cpt_j", CaptainTierEnum.Premium) { PromptVariant = PromptVariantEnum.Compact, StrictCaptain = true }
                }));
                AssertEqual((PromptVariantEnum?)PromptVariantEnum.Compact, roundTrip.Single().PromptVariant, "the variant survives voyage persistence");
                AssertEqual("cpt_j", roundTrip.Single().CaptainId, "the other override fields are unchanged");

                List<CaptainAssignmentOverride> undefined = new List<CaptainAssignmentOverride> { new CaptainAssignmentOverride("*", null, null) { PromptVariant = (PromptVariantEnum)9 } };
                AssertEqual(PromptVariantEnum.Current, PromptVariantResolver.Resolve("Worker", undefined, null), "an undefined numeric variant is ignored");

                foreach (string json in new[] { "", "   ", "{}", "{not json", "{\"promptVariant\":null}", "{\"promptVariant\":\"\"}", "{\"promptVariant\":7}", "{\"promptVariant\":\"Huge\"}", "[]" })
                {
                    Captain captain = new Captain("c") { RuntimeOptionsJson = json };
                    AssertEqual(PromptVariantEnum.Current, PromptVariantResolver.Resolve("Worker", null, captain), "captain options '" + json + "'");
                }

                AssertNull(PromptVariantResolver.Parse(null), "null parses to no variant");
                AssertNull(PromptVariantResolver.Parse("  "), "whitespace parses to no variant");
                AssertEqual((PromptVariantEnum?)PromptVariantEnum.Lean, PromptVariantResolver.Parse(" lean "), "parse is case-insensitive and trims");
                AssertEqual((PromptVariantEnum?)PromptVariantEnum.Current, PromptVariantResolver.Parse("CURRENT"), "Current parses");
                AssertThrows<ArgumentException>(() => PromptVariantResolver.Parse("Full"), "an unknown name is rejected");
            }));

            cases.Add(Case("rs8_3_selection_order_profile_then_served_context", "RS-8.3: after the voyage override and the captain option come the model profile rendering template, then the served context (Compact at or below 32768, Lean at or below 65536), then Current", TestTags.Positive, () =>
            {
                Captain plain = new Captain("plain");
                Captain lean = new Captain("lean") { RuntimeOptionsJson = "{\"promptVariant\":\"Lean\"}" };
                ModelProfileRendering compactProfile = new ModelProfileRendering { Template = "compact" };
                ModelProfileRendering badProfile = new ModelProfileRendering { Template = "Full" };
                List<CaptainAssignmentOverride> voyageCurrent = new List<CaptainAssignmentOverride> { new CaptainAssignmentOverride("*", null, null) { PromptVariant = PromptVariantEnum.Current } };
                int compactMax = PromptVariantResolver.DefaultCompactMaxContextTokens;
                int leanMax = PromptVariantResolver.DefaultLeanMaxContextTokens;
                AssertEqual(32768, compactMax, "Compact default");
                AssertEqual(65536, leanMax, "Lean default");

                PromptVariantResolution r = PromptVariantResolver.Resolve("Worker", voyageCurrent, lean, compactProfile, 8192, compactMax, leanMax);
                AssertEqual(PromptVariantEnum.Current, r.Variant, "the voyage override wins");
                AssertEqual(PromptVariantSourceEnum.VoyageOverride, r.Source);
                r = PromptVariantResolver.Resolve("Worker", null, lean, compactProfile, 8192, compactMax, leanMax);
                AssertEqual(PromptVariantEnum.Lean, r.Variant, "then the captain option");
                AssertEqual(PromptVariantSourceEnum.CaptainOption, r.Source);
                r = PromptVariantResolver.Resolve("Worker", null, plain, compactProfile, 262144, compactMax, leanMax);
                AssertEqual(PromptVariantEnum.Compact, r.Variant, "then the profile template, even with a large context");
                AssertEqual(PromptVariantSourceEnum.ModelProfile, r.Source);
                r = PromptVariantResolver.Resolve("Worker", null, plain, badProfile, 32768, compactMax, leanMax);
                AssertEqual(PromptVariantEnum.Compact, r.Variant, "an unknown profile template is ignored; 32768 served is Compact");
                AssertEqual(PromptVariantSourceEnum.ServedContext, r.Source);
                AssertEqual((int?)32768, r.ServedContextTokens);
                AssertEqual(PromptVariantEnum.Lean, PromptVariantResolver.Resolve("Worker", null, plain, null, 49152, compactMax, leanMax).Variant, "49152 served is Lean");
                AssertEqual(PromptVariantEnum.Lean, PromptVariantResolver.Resolve("Worker", null, plain, null, 65536, compactMax, leanMax).Variant, "65536 served is Lean");
                r = PromptVariantResolver.Resolve("Worker", null, plain, null, 131072, compactMax, leanMax);
                AssertEqual(PromptVariantEnum.Current, r.Variant, "131072 served is Current");
                AssertEqual(PromptVariantSourceEnum.Default, r.Source);
                AssertEqual(PromptVariantEnum.Current, PromptVariantResolver.Resolve("Worker", null, plain, null, null, compactMax, leanMax).Variant, "an unknown context is Current (frontier CLI captains are unchanged)");
                AssertEqual(PromptVariantEnum.Lean, PromptVariantResolver.Resolve("Worker", null, plain, null, 16384, 0, leanMax).Variant, "a Compact threshold of 0 turns Compact off");
                AssertEqual(PromptVariantEnum.Current, PromptVariantResolver.Resolve("Worker", null, plain, null, 16384, 0, 0).Variant, "both thresholds 0 turn the automatic choice off");
                AssertEqual(PromptVariantEnum.Current, PromptVariantResolver.Resolve("Worker", null, plain), "the three-argument overload never chooses automatically");

                ModelEndpoint configured = new ModelEndpoint { InferenceOptions = new InferenceOptions { ContextLength = 16384 }, DiscoveredContextLength = 131072 };
                ModelEndpoint discovered = new ModelEndpoint { DiscoveredContextLength = 40960 };
                ModelProfile profile = new ModelProfile();
                profile.Identity.ServedContextTokens = 24576;
                AssertEqual((int?)16384, PromptVariantResolver.ServedContextOf(configured, profile), "the configured window wins");
                AssertEqual((int?)40960, PromptVariantResolver.ServedContextOf(discovered, profile), "then the discovered window");
                AssertEqual((int?)24576, PromptVariantResolver.ServedContextOf(new ModelEndpoint(), profile), "then the profile's served context");
                AssertEqual((int?)24576, PromptVariantResolver.ServedContextOf(null, profile), "a CLI captain without an endpoint uses its profile");
                AssertNull(PromptVariantResolver.ServedContextOf(null, null), "nothing known");

                ArmadaSettings settings = new ArmadaSettings();
                AssertEqual(32768, settings.PromptVariantCompactMaxContextTokens, "setting default");
                AssertEqual(65536, settings.PromptVariantLeanMaxContextTokens, "setting default");
                AssertThrows<ArgumentOutOfRangeException>(() => settings.PromptVariantCompactMaxContextTokens = -1, "negative rejected");
                AssertThrows<ArgumentOutOfRangeException>(() => settings.PromptVariantLeanMaxContextTokens = 2097153, "above the maximum rejected");
            }));

            cases.Add(CaseAsync("rs8_3_generation_uses_profile_template_and_served_context", "RS-8.3: instruction generation renders the model profile's template, and a small served context renders Compact; the captain option still wins", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MissionService service = CreateMissionService(CreateLogging(), testDb.Driver, out IPromptTemplateService templates);

                    ModelProfile profile = new ModelProfile { Rendering = new ModelProfileRendering { Template = "Compact" } };
                    profile.Identity.Runtime = "ClaudeCode";
                    profile = await testDb.Driver.ModelProfiles.CreateAsync(profile, "rs83-" + Guid.NewGuid().ToString("N")).ConfigureAwait(false);
                    Fixture byProfile = CreateFixture(PersonaCatalog.Worker);
                    byProfile.Captain.ModelProfileId = profile.Id;
                    Dictionary<string, string> profileParams = MissionPromptBuilder.BuildTemplateParams(byProfile.Mission, byProfile.Vessel, byProfile.Captain);
                    string compact = await templates.RenderAsync("persona.worker.compact", profileParams).ConfigureAwait(false);
                    string lean = await templates.RenderAsync("persona.worker.lean", profileParams).ConfigureAwait(false);
                    AssertContains(compact, await GenerateAsync(service, byProfile, null).ConfigureAwait(false), "the profile template renders Compact");
                    AssertContains(lean, await GenerateAsync(service, byProfile, "{\"promptVariant\":\"Lean\"}").ConfigureAwait(false), "the captain option beats the profile template");

                    ModelEndpoint endpoint = new ModelEndpoint { Name = "rs83-small", Model = "small", InferenceOptions = new InferenceOptions { ContextLength = 32768 } };
                    endpoint = await testDb.Driver.ModelEndpoints.CreateAsync(endpoint).ConfigureAwait(false);
                    Fixture api = CreateFixture(PersonaCatalog.Worker);
                    api.Captain.Runtime = AgentRuntimeEnum.ApiEndpoint;
                    api.Captain.ModelEndpointId = endpoint.Id;
                    Dictionary<string, string> apiParams = MissionPromptBuilder.BuildTemplateParams(api.Mission, api.Vessel, api.Captain);
                    string apiCompact = await templates.RenderAsync("persona.worker.compact", apiParams).ConfigureAwait(false);
                    AssertContains(apiCompact, await GenerateAsync(service, api, null).ConfigureAwait(false), "a 32768-token endpoint renders Compact automatically");
                    AssertFalse((await GenerateAsync(service, api, "{\"promptVariant\":\"Current\"}").ConfigureAwait(false)).Contains(apiCompact, StringComparison.Ordinal),
                        "an explicit Current option keeps Current");

                    Fixture frontier = CreateFixture(PersonaCatalog.Worker);
                    AssertFalse((await GenerateAsync(service, frontier, null).ConfigureAwait(false)).Contains(compact, StringComparison.Ordinal),
                        "a captain with no profile and no endpoint keeps Current");
                }
            }));

            cases.Add(CaseAsync("generation_uses_voyage_override_before_captain_option", "Instruction generation renders the voyage override's variant over the captain's option", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MissionService service = CreateMissionService(CreateLogging(), testDb.Driver, out IPromptTemplateService templates);
                    Voyage voyage = new Voyage("Variant voyage");
                    voyage.CaptainOverridesJson = MissionService.SerializeCaptainOverrides(new List<CaptainAssignmentOverride>
                    {
                        new CaptainAssignmentOverride("Judge", null, null) { PromptVariant = PromptVariantEnum.Compact }
                    });
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Fixture judge = CreateFixture("Judge");
                    judge.Mission.VoyageId = voyage.Id;
                    string judgeDoc = await GenerateAsync(service, judge, "{\"promptVariant\":\"Lean\"}").ConfigureAwait(false);
                    Dictionary<string, string> judgeParams = MissionPromptBuilder.BuildTemplateParams(judge.Mission, judge.Vessel, judge.Captain);
                    AssertContains(await templates.RenderAsync("persona.judge.compact", judgeParams).ConfigureAwait(false), judgeDoc, "the voyage override's Compact wins for the Judge");

                    Fixture worker = CreateFixture("Worker");
                    worker.Mission.VoyageId = voyage.Id;
                    string workerDoc = await GenerateAsync(service, worker, "{\"promptVariant\":\"Lean\"}").ConfigureAwait(false);
                    Dictionary<string, string> workerParams = MissionPromptBuilder.BuildTemplateParams(worker.Mission, worker.Vessel, worker.Captain);
                    AssertContains(await templates.RenderAsync("persona.worker.lean", workerParams).ConfigureAwait(false), workerDoc, "no override for the Worker: the captain's Lean applies");

                    Fixture missingVoyage = CreateFixture("Worker");
                    missingVoyage.Mission.VoyageId = "vyg_missing";
                    string missingDoc = await GenerateAsync(service, missingVoyage, "{\"promptVariant\":\"Compact\"}").ConfigureAwait(false);
                    Dictionary<string, string> missingParams = MissionPromptBuilder.BuildTemplateParams(missingVoyage.Mission, missingVoyage.Vessel, missingVoyage.Captain);
                    AssertContains(await templates.RenderAsync("persona.worker.compact", missingParams).ConfigureAwait(false), missingDoc, "a missing voyage leaves the captain's option");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Prompt Variants",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<string> BodyAsync(string persona, PromptVariantEnum variant, Dictionary<string, string> templateParams, IPromptTemplateService templates)
        {
            PromptTemplate? chosen = await MissionPromptBuilder.ChoosePersonaTemplateAsync(persona, null, variant, templates).ConfigureAwait(false);
            return await MissionPromptBuilder.RenderPersonaPromptBodyAsync(persona, chosen, templateParams, templates).ConfigureAwait(false);
        }

        private sealed class PersonaCase
        {
            public PersonaCase(string name, string templateName, string contract)
            {
                Name = name;
                TemplateName = templateName;
                Contract = contract;
            }

            public string Name { get; }

            public string TemplateName { get; }

            public string Contract { get; }
        }

        private sealed class Fixture
        {
            public Mission Mission { get; set; } = null!;

            public Vessel Vessel { get; set; } = null!;

            public Captain Captain { get; set; } = null!;
        }

        private static Fixture CreateFixture(string persona)
        {
            Vessel vessel = new Vessel("VariantVessel", "https://github.com/test/widgets");
            vessel.ProjectContext = ProjectContext;
            vessel.StyleGuide = StyleGuide;

            Mission mission = new Mission("Widget lookup", "Return 404 for unknown widgets.\n\n" + AcceptanceCriteria);
            mission.Persona = persona;
            mission.BranchName = "armada/variant-test";

            Captain captain = new Captain("variant-captain");
            captain.Runtime = AgentRuntimeEnum.ClaudeCode;

            return new Fixture { Mission = mission, Vessel = vessel, Captain = captain };
        }

        private static async Task<string> GenerateAsync(MissionService service, Fixture fixture, string? runtimeOptionsJson)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "armada_variant_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                await File.WriteAllTextAsync(Path.Combine(tempDir, "CLAUDE.md"), ExistingRules).ConfigureAwait(false);
                fixture.Captain.RuntimeOptionsJson = runtimeOptionsJson;
                await service.GenerateClaudeMdAsync(tempDir, fixture.Mission, fixture.Vessel, fixture.Captain).ConfigureAwait(false);
                return await File.ReadAllTextAsync(Path.Combine(tempDir, MissionPromptBuilder.GetInstructionsFileName(fixture.Captain.Runtime.ToString()))).ConfigureAwait(false);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static MissionService CreateMissionService(LoggingModule logging, DatabaseDriver db, out IPromptTemplateService templates)
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_test_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_test_repos_" + Guid.NewGuid().ToString("N"));
            StubGitService git = new StubGitService();
            IDockService dockService = new DockService(logging, db, settings, git);
            ICaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
            templates = new PromptTemplateService(db, logging);
            return new MissionService(logging, db, settings, dockService, captainService, templates);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
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
