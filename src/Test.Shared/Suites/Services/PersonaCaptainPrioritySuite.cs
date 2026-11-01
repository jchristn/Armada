namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Pure matching, policy validation, and provider-neutral persistence regression descriptors.</summary>
    public sealed class PersonaCaptainPrioritySuite : IArmadaTestSuite
    {
        private const string _SuiteId = "Services.PersonaCaptainPriority";

        /// <summary>Build all priority descriptors for every Touchstone runner.</summary>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("inactive_priority_reactivation", () =>
            {
                Persona persona = Hierarchy();
                Captain captain = new Captain("disabled", AgentRuntimeEnum.Codex) { Active = false };
                AssertNull(PersonaCaptainPrioritySelector.Select(persona, new[] { captain }));
                AssertNull(CaptainTierSelector.Select(new[] { captain }, null));
                captain.Active = true;
                AssertEqual(captain.Id, PersonaCaptainPrioritySelector.Select(persona, new[] { captain })?.Id);
                AssertEqual(captain.Id, CaptainTierSelector.Select(new[] { captain }, null)?.Id);
            }));
            cases.Add(Case("ordered_runtime_classes", () =>
            {
                Persona persona = Hierarchy();
                Captain codex = new Captain("codex", AgentRuntimeEnum.Codex);
                Captain claude = new Captain("claude", AgentRuntimeEnum.ClaudeCode);
                AssertEqual(codex.Id, PersonaCaptainPrioritySelector.Select(persona, new[] { claude, codex })?.Id);
                persona.CaptainPriorities.Reverse();
                AssertEqual(claude.Id, PersonaCaptainPrioritySelector.Select(persona, new[] { codex, claude })?.Id);
            }));
            cases.Add(Case("fallthrough_and_wait", () =>
            {
                Persona persona = Hierarchy();
                Captain claude = new Captain("claude", AgentRuntimeEnum.ClaudeCode);
                AssertEqual(claude.Id, PersonaCaptainPrioritySelector.Select(persona, new[] { claude })?.Id);
                persona.PriorityAvailability = PersonaPriorityAvailabilityEnum.WaitForFirstPriority;
                persona.PriorityExhaustion = PersonaPriorityExhaustionEnum.UseEligibleCaptain;
                AssertNull(PersonaCaptainPrioritySelector.Select(persona, new[] { claude }));
                AssertFalse(PersonaCaptainPrioritySelector.AllowsGeneralFallback(persona));
            }));
            cases.Add(Case("exhaustion_is_explicit", () =>
            {
                Persona persona = Hierarchy();
                AssertNull(PersonaCaptainPrioritySelector.Select(persona, Array.Empty<Captain>()));
                AssertFalse(PersonaCaptainPrioritySelector.AllowsGeneralFallback(persona));
                persona.PriorityExhaustion = PersonaPriorityExhaustionEnum.UseEligibleCaptain;
                AssertTrue(PersonaCaptainPrioritySelector.AllowsGeneralFallback(persona));
                persona.CaptainPriorities.Clear();
                AssertTrue(PersonaCaptainPrioritySelector.AllowsGeneralFallback(persona));
            }));
            cases.Add(Case("combined_mux_fields", () =>
            {
                PersonaCaptainPriority priority = new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Mux, Model = "gpt-oss:120b", Endpoint = "local", MinimumTier = CaptainTierEnum.Economy };
                Captain captain = new Captain("mux", AgentRuntimeEnum.Mux) { Model = "GPT-OSS:120B", RuntimeOptionsJson = "{\"endpoint\":\"LOCAL\"}" };
                AssertTrue(PersonaCaptainPrioritySelector.Matches(priority, captain));
                captain.RuntimeOptionsJson = "{\"endpoint\":\"other\"}";
                AssertFalse(PersonaCaptainPrioritySelector.Matches(priority, captain));
                captain.RuntimeOptionsJson = "invalid json";
                AssertFalse(PersonaCaptainPrioritySelector.Matches(priority, captain));
                captain.RuntimeOptionsJson = null;
                AssertFalse(PersonaCaptainPrioritySelector.Matches(priority, captain));
                captain.RuntimeOptionsJson = "{\"endpoint\":\"local\"}";
                captain.Model = "other";
                AssertFalse(PersonaCaptainPrioritySelector.Matches(priority, captain));
                captain.Model = "gpt-oss:120b";
                priority.MinimumTier = CaptainTierEnum.Premium;
                AssertFalse(PersonaCaptainPrioritySelector.Matches(priority, captain));
                captain.Tier = CaptainTierEnum.Premium;
                AssertTrue(PersonaCaptainPrioritySelector.Matches(priority, captain));
                captain.Runtime = AgentRuntimeEnum.Codex;
                AssertFalse(PersonaCaptainPrioritySelector.Matches(priority, captain));
            }));
            cases.Add(Case("api_endpoint_exact_identity_and_policies", () =>
            {
                PersonaCaptainPriority priority = new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ApiEndpoint, Endpoint = "mep_Selected" };
                Captain selected = new Captain("selected", AgentRuntimeEnum.ApiEndpoint) { ModelEndpointId = "mep_Selected", Model = "same-model" };
                Captain other = new Captain("other", AgentRuntimeEnum.ApiEndpoint) { ModelEndpointId = "mep_other", Model = "same-model" };
                Persona persona = new Persona("EndpointRole", "persona.worker") { CaptainPriorities = new List<PersonaCaptainPriority> { priority, new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex } } };
                AssertNull(PersonaCaptainPrioritySelector.Validate(persona));
                AssertTrue(PersonaCaptainPrioritySelector.Matches(priority, selected));
                AssertFalse(PersonaCaptainPrioritySelector.Matches(priority, other));
                AssertEqual(selected.Id, PersonaCaptainPrioritySelector.Select(persona, new[] { other, selected })?.Id);
                selected.ModelEndpointId = "mep_selected";
                AssertFalse(PersonaCaptainPrioritySelector.Matches(priority, selected));
                selected.ModelEndpointId = "mep_Selected";
                selected.Runtime = AgentRuntimeEnum.Mux;
                AssertFalse(PersonaCaptainPrioritySelector.Matches(priority, selected));
                Captain fallback = new Captain("fallback", AgentRuntimeEnum.Codex);
                AssertEqual(fallback.Id, PersonaCaptainPrioritySelector.Select(persona, new[] { other, fallback })?.Id);
                persona.PriorityAvailability = PersonaPriorityAvailabilityEnum.WaitForFirstPriority;
                AssertNull(PersonaCaptainPrioritySelector.Select(persona, new[] { other, fallback }));
                AssertTrue(PersonaCaptainPrioritySelector.Matches(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ApiEndpoint }, other));
            }));
            cases.Add(Case("model_and_tier_only_classes", () =>
            {
                Captain captain = new Captain("captain", AgentRuntimeEnum.Gemini) { Model = "example", Tier = CaptainTierEnum.Premium };
                AssertTrue(PersonaCaptainPrioritySelector.Matches(new PersonaCaptainPriority { Model = "EXAMPLE" }, captain));
                AssertTrue(PersonaCaptainPrioritySelector.Matches(new PersonaCaptainPriority { MinimumTier = CaptainTierEnum.Standard }, captain));
                Persona bounded = new Persona();
                bounded.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Mux, Model = new string('m', 1024), Endpoint = new string('e', 1024) });
                AssertNull(PersonaCaptainPrioritySelector.Validate(bounded));
                bounded.CaptainPriorities[0].Model += "m";
                AssertNotNull(PersonaCaptainPrioritySelector.Validate(bounded));
                bounded.CaptainPriorities[0].Model = "model";
                bounded.CaptainPriorities[0].Endpoint += "e";
                AssertNotNull(PersonaCaptainPrioritySelector.Validate(bounded));
                AssertFalse(PersonaCaptainPrioritySelector.Matches(new PersonaCaptainPriority { Model = "different" }, captain));
            }));
            cases.Add(Case("stable_ties_and_persona_preference", () =>
            {
                Persona persona = Hierarchy();
                Captain first = new Captain("a", AgentRuntimeEnum.Codex) { Id = "cpt_a" };
                Captain second = new Captain("b", AgentRuntimeEnum.Codex) { Id = "cpt_b" };
                AssertEqual(first.Id, PersonaCaptainPrioritySelector.Select(persona, new[] { second, first })?.Id);
                second.PreferredPersona = persona.Name;
                AssertEqual(second.Id, PersonaCaptainPrioritySelector.Select(persona, new[] { first, second })?.Id);
            }));
            cases.Add(Case("typed_json_roundtrip", () =>
            {
                Persona persona = Hierarchy();
                persona.PriorityAvailability = PersonaPriorityAvailabilityEnum.WaitForFirstPriority;
                Persona copy = JsonSerializer.Deserialize<Persona>(JsonSerializer.Serialize(persona))!;
                AssertEqual(2, copy.CaptainPriorities.Count);
                AssertEqual(AgentRuntimeEnum.Codex, copy.CaptainPriorities[0].Runtime);
                AssertEqual(persona.PriorityAvailability, copy.PriorityAvailability);
                AssertEqual(0, JsonSerializer.Deserialize<Persona>("{\"CaptainPriorities\":null}")!.CaptainPriorities.Count);
            }));
            cases.Add(Case("reject_empty", () =>
            {
                Persona persona = new Persona();
                persona.CaptainPriorities.Add(new PersonaCaptainPriority());
                AssertNotNull(PersonaCaptainPrioritySelector.Validate(persona));
                AssertNull(PersonaCaptainPrioritySelector.Select(persona, new[] { new Captain() }));
            }));
            cases.Add(Case("reject_null", () =>
            {
                Persona persona = new Persona();
                persona.CaptainPriorities.Add(null!);
                AssertNotNull(PersonaCaptainPrioritySelector.Validate(persona));
                AssertNull(PersonaCaptainPrioritySelector.Select(persona, new[] { new Captain() }));
            }));
            cases.Add(Case("reject_runtime", () =>
            {
                Persona persona = new Persona();
                persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = (AgentRuntimeEnum)999 });
                AssertNotNull(PersonaCaptainPrioritySelector.Validate(persona));
                AssertNull(PersonaCaptainPrioritySelector.Select(persona, new[] { new Captain() }));
            }));
            cases.Add(Case("reject_tier", () =>
            {
                Persona persona = new Persona();
                persona.CaptainPriorities.Add(new PersonaCaptainPriority { MinimumTier = (CaptainTierEnum)999 });
                AssertNotNull(PersonaCaptainPrioritySelector.Validate(persona));
                AssertNull(PersonaCaptainPrioritySelector.Select(persona, new[] { new Captain() }));
            }));
            cases.Add(Case("reject_blank_model", () =>
            {
                Persona persona = new Persona();
                persona.CaptainPriorities.Add(new PersonaCaptainPriority { Model = " " });
                AssertNotNull(PersonaCaptainPrioritySelector.Validate(persona));
                AssertNull(PersonaCaptainPrioritySelector.Select(persona, new[] { new Captain() }));
            }));
            cases.Add(Case("reject_blank_endpoint", () =>
            {
                Persona persona = new Persona();
                persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Mux, Endpoint = " " });
                AssertNotNull(PersonaCaptainPrioritySelector.Validate(persona));
                AssertNull(PersonaCaptainPrioritySelector.Select(persona, new[] { new Captain() }));
            }));
            cases.Add(Case("reject_endpoint_runtime", () =>
            {
                Persona persona = new Persona();
                persona.CaptainPriorities.Add(new PersonaCaptainPriority { Endpoint = "local", Runtime = AgentRuntimeEnum.Codex });
                AssertNotNull(PersonaCaptainPrioritySelector.Validate(persona));
                AssertNull(PersonaCaptainPrioritySelector.Select(persona, new[] { new Captain() }));
            }));
            cases.Add(Case("reject_policy_enums", () =>
            {
                Persona persona = Hierarchy();
                persona.PriorityAvailability = (PersonaPriorityAvailabilityEnum)999;
                AssertNotNull(PersonaCaptainPrioritySelector.Validate(persona));
                persona.PriorityAvailability = PersonaPriorityAvailabilityEnum.FallThrough;
                persona.PriorityExhaustion = (PersonaPriorityExhaustionEnum)999;
                AssertNotNull(PersonaCaptainPrioritySelector.Validate(persona));
            }));
            cases.Add(new TestCaseDescriptor(_SuiteId, "database_roundtrip_replace_clear_delete", "All persona reads preserve relational priorities", async token =>
            {
                using (TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    Persona persona = Hierarchy();
                    persona.PriorityAvailability = PersonaPriorityAvailabilityEnum.WaitForFirstPriority;
                    persona.PriorityExhaustion = PersonaPriorityExhaustionEnum.UseEligibleCaptain;
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Mux, Endpoint = "local", Model = "gpt-oss:120b", MinimumTier = CaptainTierEnum.Economy });
                    persona.CaptainPriorities.Insert(0, new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ApiEndpoint, Endpoint = "mep_selected" });
                    await db.Driver.Personas.CreateAsync(persona, token).ConfigureAwait(false);
                    Persona read = (await db.Driver.Personas.ReadAsync(persona.Id, token).ConfigureAwait(false))!;
                    AssertEqual(4, read.CaptainPriorities.Count);
                    AssertEqual("mep_selected", read.CaptainPriorities[0].Endpoint);
                    AssertEqual(AgentRuntimeEnum.Codex, read.CaptainPriorities[1].Runtime);
                    AssertEqual("local", read.CaptainPriorities[3].Endpoint);
                    AssertEqual("gpt-oss:120b", read.CaptainPriorities[3].Model);
                    AssertEqual(CaptainTierEnum.Economy, read.CaptainPriorities[3].MinimumTier);
                    AssertEqual(persona.PriorityAvailability, read.PriorityAvailability);
                    AssertEqual(persona.PriorityExhaustion, read.PriorityExhaustion);
                    AssertEqual(4, (await db.Driver.Personas.ReadByNameAsync(persona.Name, token).ConfigureAwait(false))!.CaptainPriorities.Count);
                    AssertEqual(4, (await db.Driver.Personas.EnumerateAsync(token).ConfigureAwait(false)).Single(p => p.Id == persona.Id).CaptainPriorities.Count);
                    AssertEqual(4, (await db.Driver.Personas.EnumerateAsync(new EnumerationQuery(), token).ConfigureAwait(false)).Objects.Single(p => p.Id == persona.Id).CaptainPriorities.Count);
                    read.CaptainPriorities.Reverse();
                    await db.Driver.Personas.UpdateAsync(read, token).ConfigureAwait(false);
                    AssertEqual(AgentRuntimeEnum.Mux, (await db.Driver.Personas.ReadAsync(persona.Id, token).ConfigureAwait(false))!.CaptainPriorities[0].Runtime);
                    read.CaptainPriorities.Clear();
                    await db.Driver.Personas.UpdateAsync(read, token).ConfigureAwait(false);
                    AssertEqual(0, (await db.Driver.Personas.ReadAsync(persona.Id, token).ConfigureAwait(false))!.CaptainPriorities.Count);
                    read.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Mux });
                    await db.Driver.Personas.UpdateAsync(read, token).ConfigureAwait(false);
                    await db.Driver.Personas.DeleteAsync(read.Id, token).ConfigureAwait(false);
                    AssertNull(await db.Driver.Personas.ReadAsync(read.Id, token).ConfigureAwait(false));
                }
            }));
            return new TestSuiteDescriptor(_SuiteId, "Persona captain priorities", cases);
        }

        private static Persona Hierarchy()
        {
            return new Persona("PriorityRole", "persona.worker")
            {
                CaptainPriorities = new List<PersonaCaptainPriority>
                {
                    new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex },
                    new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ClaudeCode }
                }
            };
        }

        private static TestCaseDescriptor Case(string id, Action body)
        {
            return new TestCaseDescriptor(_SuiteId, id, id.Replace('_', ' '), token =>
            {
                token.ThrowIfCancellationRequested();
                body();
                return Task.CompletedTask;
            });
        }
    }
}
