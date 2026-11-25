namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="CaptainTierSelector"/>. Positive cases confirm model classification, the
    /// explicit-tier override, and lowest-eligible selection; negative cases confirm no captain is chosen
    /// when none can meet the required tier and that unknown models fall back to Standard.
    /// </summary>
    public sealed class CaptainTierSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the captain-tier suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            // ---- Model classification ----
            cases.Add(Case("classify_premium_models", "Premium models classify as Premium", TestTags.Positive, () =>
            {
                AssertEqual(CaptainTierEnum.Premium, CaptainTierSelector.ClassifyModel("claude-opus-4-8"));
                AssertEqual(CaptainTierEnum.Premium, CaptainTierSelector.ClassifyModel("gpt-5.2"));
                AssertEqual(CaptainTierEnum.Premium, CaptainTierSelector.ClassifyModel("o3-pro"));
                AssertEqual(CaptainTierEnum.Premium, CaptainTierSelector.ClassifyModel("gemini-2.5-pro"));
            }));

            cases.Add(Case("classify_model_table", "Model names across vendors and versions classify as intended (GPT 6 and later are Premium; mini, nano, and flash variants stay Economy; o3 and o4 match only as a model name)", TestTags.Positive, () =>
            {
                Dictionary<string, CaptainTierEnum> table = new Dictionary<string, CaptainTierEnum>(StringComparer.Ordinal)
                {
                    ["claude-opus-5-5"] = CaptainTierEnum.Premium,
                    ["claude-opus-4-8"] = CaptainTierEnum.Premium,
                    ["gpt-5"] = CaptainTierEnum.Premium,
                    ["gpt-5.2"] = CaptainTierEnum.Premium,
                    ["gpt-6.1"] = CaptainTierEnum.Premium,
                    ["gpt-6"] = CaptainTierEnum.Premium,
                    ["gpt-7.0-codex"] = CaptainTierEnum.Premium,
                    ["gpt-10"] = CaptainTierEnum.Premium,
                    ["openai/gpt-6.1"] = CaptainTierEnum.Premium,
                    ["o3"] = CaptainTierEnum.Premium,
                    ["o3-pro"] = CaptainTierEnum.Premium,
                    ["openai/o4"] = CaptainTierEnum.Premium,
                    ["gemini-2.5-pro"] = CaptainTierEnum.Premium,
                    ["gemini-3-pro"] = CaptainTierEnum.Premium,
                    ["gpt-6.1-mini"] = CaptainTierEnum.Economy,
                    ["gpt-5-nano"] = CaptainTierEnum.Economy,
                    ["o4-mini"] = CaptainTierEnum.Economy,
                    ["gemini-2.5-flash"] = CaptainTierEnum.Economy,
                    ["claude-haiku-5-5"] = CaptainTierEnum.Economy,
                    ["gpt-oss:120b"] = CaptainTierEnum.Economy,
                    ["deepseek-v4.1-flash"] = CaptainTierEnum.Economy,
                    ["qwen3:8b"] = CaptainTierEnum.Economy,
                    ["claude-sonnet-5-5"] = CaptainTierEnum.Standard,
                    ["gpt-4.1"] = CaptainTierEnum.Standard,
                    ["gpt-4o"] = CaptainTierEnum.Standard,
                    ["qwen3.8"] = CaptainTierEnum.Standard,
                    ["qwen3.8:27b"] = CaptainTierEnum.Standard,
                    ["inkling-2-mini"] = CaptainTierEnum.Economy,
                    ["glm-5.3"] = CaptainTierEnum.Standard,
                    ["kimi-k3"] = CaptainTierEnum.Standard,
                    ["deepseek-v4-pro:0813"] = CaptainTierEnum.Standard,
                    ["gemini-2.5"] = CaptainTierEnum.Standard,
                    ["qwen3-o3-merge"] = CaptainTierEnum.Standard
                };
                List<string> wrong = table.Where(pair => CaptainTierSelector.ClassifyModel(pair.Key) != pair.Value)
                    .Select(pair => pair.Key + " => " + CaptainTierSelector.ClassifyModel(pair.Key) + " (want " + pair.Value + ")").ToList();
                AssertTrue(wrong.Count == 0, String.Join("; ", wrong));
            }));

            cases.Add(Case("classify_economy_models", "Economy models classify as Economy", TestTags.Positive, () =>
            {
                AssertEqual(CaptainTierEnum.Economy, CaptainTierSelector.ClassifyModel("claude-haiku-4-5"));
                AssertEqual(CaptainTierEnum.Economy, CaptainTierSelector.ClassifyModel("gpt-5-mini"));
                AssertEqual(CaptainTierEnum.Economy, CaptainTierSelector.ClassifyModel("gemini-2.0-flash"));
                AssertEqual(CaptainTierEnum.Economy, CaptainTierSelector.ClassifyModel("gpt-oss-120b"));
            }));

            cases.Add(Case("classify_standard_models", "Sonnet/gpt-4 classify as Standard", TestTags.Positive, () =>
            {
                AssertEqual(CaptainTierEnum.Standard, CaptainTierSelector.ClassifyModel("claude-sonnet-5"));
                AssertEqual(CaptainTierEnum.Standard, CaptainTierSelector.ClassifyModel("gpt-4.1"));
            }));

            cases.Add(Case("classify_unknown_defaults_standard", "Unknown/blank models default to Standard", TestTags.Negative, () =>
            {
                AssertEqual(CaptainTierEnum.Standard, CaptainTierSelector.ClassifyModel("some-unknown-model"));
                AssertEqual(CaptainTierEnum.Standard, CaptainTierSelector.ClassifyModel(null));
                AssertEqual(CaptainTierEnum.Standard, CaptainTierSelector.ClassifyModel("   "));
            }));

            // ---- Effective tier ----
            cases.Add(Case("explicit_tier_overrides_model", "Explicit tier overrides model classification", TestTags.Positive, () =>
            {
                Captain economyModelPremiumTier = MakeCaptain("claude-haiku-4-5", CaptainTierEnum.Premium);
                AssertEqual(CaptainTierEnum.Premium, CaptainTierSelector.EffectiveTier(economyModelPremiumTier));
            }));

            cases.Add(Case("effective_tier_null_captain_standard", "Null captain resolves to Standard", TestTags.Negative, () =>
            {
                AssertEqual(CaptainTierEnum.Standard, CaptainTierSelector.EffectiveTier(null!));
            }));

            cases.Add(Case("classify_small_parameter_count_economy", "A parameter count of 8B or less classifies as Economy", TestTags.Positive, () =>
            {
                AssertEqual(CaptainTierEnum.Economy, CaptainTierSelector.ClassifyModel("qwen2.5-coder:7b"));
                AssertEqual(CaptainTierEnum.Economy, CaptainTierSelector.ClassifyModel("llama3.2:3b"));
                AssertEqual(CaptainTierEnum.Economy, CaptainTierSelector.ClassifyModel("gemma-2b"));
                AssertEqual(CaptainTierEnum.Economy, CaptainTierSelector.ClassifyModel("phi3:3.8b"));
                AssertEqual(CaptainTierEnum.Economy, CaptainTierSelector.ClassifyModel("deepseek-r1:8b"));
                AssertEqual(CaptainTierEnum.Standard, CaptainTierSelector.ClassifyModel("qwen3.8:27b"));
                AssertEqual(CaptainTierEnum.Standard, CaptainTierSelector.ClassifyModel("llama3.1:70b"));
                AssertEqual(CaptainTierEnum.Standard, CaptainTierSelector.ClassifyModel("deepseek-v4.1"));
            }));

            // ---- Vendor default model ----
            cases.Add(Case("vendor_cli_default_model_handles_any_tier", "A Claude Code, Codex or Gemini captain with no model can take Premium work but ranks Standard", TestTags.Positive, () =>
            {
                foreach (AgentRuntimeEnum runtime in new[] { AgentRuntimeEnum.ClaudeCode, AgentRuntimeEnum.Codex, AgentRuntimeEnum.Gemini })
                {
                    Captain captain = new Captain();
                    captain.Runtime = runtime;
                    AssertEqual(CaptainTierEnum.Standard, CaptainTierSelector.EffectiveTier(captain));
                    AssertTrue(CaptainTierSelector.CanHandle(captain, CaptainTierEnum.Premium), runtime + " default model should take Premium work");
                    AssertEqual(captain.Id, CaptainTierSelector.Select(new List<Captain> { captain }, CaptainTierEnum.Premium)!.Id);
                }
            }));

            cases.Add(Case("vendor_default_ranking_unchanged_for_standard_work", "A no-model Claude captain still competes as Standard, below an explicit Premium captain", TestTags.Positive, () =>
            {
                Captain vendorDefault = new Captain();
                Captain premium = MakeCaptain("claude-opus-4-8", null);
                Captain? chosen = CaptainTierSelector.Select(new List<Captain> { premium, vendorDefault }, CaptainTierEnum.Standard);
                AssertEqual(vendorDefault.Id, chosen!.Id);
            }));

            cases.Add(Case("other_runtime_default_model_not_premium", "Mux, OpenCode, Pi, Cursor and API captains with no model stay Standard for Premium work", TestTags.Negative, () =>
            {
                foreach (AgentRuntimeEnum runtime in new[] { AgentRuntimeEnum.Mux, AgentRuntimeEnum.OpenCode, AgentRuntimeEnum.Pi, AgentRuntimeEnum.Cursor, AgentRuntimeEnum.ApiEndpoint, AgentRuntimeEnum.Custom })
                {
                    Captain captain = new Captain();
                    captain.Runtime = runtime;
                    AssertFalse(CaptainTierSelector.CanHandle(captain, CaptainTierEnum.Premium), runtime + " should not take Premium work");
                    AssertTrue(CaptainTierSelector.CanHandle(captain, CaptainTierEnum.Standard), runtime + " should take Standard work");
                }
            }));

            cases.Add(Case("explicit_tier_declared_wins_over_vendor_default", "An explicit tier on a no-model Claude captain is taken as declared", TestTags.Negative, () =>
            {
                Captain declared = new Captain();
                declared.Tier = CaptainTierEnum.Economy;
                AssertFalse(CaptainTierSelector.CanHandle(declared, CaptainTierEnum.Standard));
                AssertFalse(CaptainTierSelector.CanHandle(null!, CaptainTierEnum.Economy));
            }));

            // ---- Selection ----
            cases.Add(Case("select_prefers_lowest_eligible", "Select prefers lowest eligible tier at/above required", TestTags.Positive, () =>
            {
                Captain economy = MakeCaptain("claude-haiku-4-5", null);
                Captain standard = MakeCaptain("claude-sonnet-5", null);
                Captain premium = MakeCaptain("claude-opus-4-8", null);
                List<Captain> pool = new List<Captain> { premium, economy, standard };

                // Standard mission: should pick Standard, not Premium.
                Captain? chosen = CaptainTierSelector.Select(pool, CaptainTierEnum.Standard);
                AssertNotNull(chosen);
                AssertEqual(standard.Id, chosen!.Id);

                // Economy mission: should pick Economy.
                Captain? cheap = CaptainTierSelector.Select(pool, CaptainTierEnum.Economy);
                AssertNotNull(cheap);
                AssertEqual(economy.Id, cheap!.Id);
            }));

            cases.Add(Case("select_falls_back_upward", "Select falls back upward when required tier idle-empty", TestTags.Positive, () =>
            {
                Captain premium = MakeCaptain("claude-opus-4-8", null);
                List<Captain> pool = new List<Captain> { premium };

                // Only a Premium captain is idle; a Standard mission may use it.
                Captain? chosen = CaptainTierSelector.Select(pool, CaptainTierEnum.Standard);
                AssertNotNull(chosen);
                AssertEqual(premium.Id, chosen!.Id);
            }));

            cases.Add(Case("select_null_when_none_meet_tier", "Select returns null when no captain meets required tier", TestTags.Negative, () =>
            {
                Captain economy = MakeCaptain("claude-haiku-4-5", null);
                List<Captain> pool = new List<Captain> { economy };

                // Premium mission, only Economy idle -> no eligible captain.
                Captain? chosen = CaptainTierSelector.Select(pool, CaptainTierEnum.Premium);
                AssertNull(chosen);
            }));

            cases.Add(Case("select_null_on_empty_pool", "Select returns null on empty/null pool", TestTags.Negative, () =>
            {
                AssertNull(CaptainTierSelector.Select(new List<Captain>(), CaptainTierEnum.Standard));
                AssertNull(CaptainTierSelector.Select(null!, CaptainTierEnum.Standard));
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.CaptainTier",
                displayName: "Captain Tier Selector",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static Captain MakeCaptain(string model, CaptainTierEnum? tier)
        {
            Captain captain = new Captain();
            captain.Model = model;
            captain.Tier = tier;
            return captain;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.CaptainTier",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
