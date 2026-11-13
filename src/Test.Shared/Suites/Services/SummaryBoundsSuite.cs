namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Bounds on the token-usage and request-history summaries (docs/ENUMERATION.md): a window that would produce more
    /// than <see cref="CollectionLimits.MaxSummaryBuckets"/> buckets, or a bucket width past the ceiling, is rejected
    /// before any record is read, and the token-usage per-model breakdowns list at most ModelLimit models with the rest
    /// summed into an explicit OtherModels aggregate, so no usage is dropped.
    /// </summary>
    public sealed class SummaryBoundsSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.SummaryBounds";
        private static readonly DateTime _Anchor = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("token_usage_window_at_the_limit_is_accepted_and_one_more_rejected", "Token usage: exactly 1000 buckets is accepted; 1001 is rejected naming Buckets", TestTags.Negative, () =>
            {
                TokenUsageQuery atLimit = new TokenUsageQuery { FromUtc = _Anchor, ToUtc = _Anchor.AddMinutes(999), BucketMinutes = 1 };
                TokenUsageSummaryResult result = TokenUsageSummaryBuilder.Build(new List<TokenUsageRecord>(), atLimit);
                AssertEqual(CollectionLimits.MaxSummaryBuckets, result.Buckets.Count, "gap-filled to the limit");

                TokenUsageQuery over = new TokenUsageQuery { FromUtc = _Anchor, ToUtc = _Anchor.AddMinutes(1000), BucketMinutes = 1 };
                CollectionLimitExceededException ex = ThrowsLimit(() => TokenUsageSummaryBuilder.ValidateWindow(over));
                AssertEqual("Buckets", ex.Field, "field");
                AssertEqual(CollectionLimits.MaxSummaryBuckets, ex.Limit, "limit");
                AssertEqual(1001L, ex.Actual, "actual");
                ThrowsLimit(() => TokenUsageSummaryBuilder.Build(new List<TokenUsageRecord>(), over));
            }));

            cases.Add(Case("token_usage_fractional_and_huge_widths", "Token usage: a year at 30-second buckets is rejected, and a width past 366 days is out of range", TestTags.Negative, () =>
            {
                ThrowsLimit(() => TokenUsageSummaryBuilder.ValidateWindow(new TokenUsageQuery { FromUtc = _Anchor.AddDays(-365), ToUtc = _Anchor, BucketMinutes = 0.5 }));
                AssertThrows<ArgumentOutOfRangeException>(() => TokenUsageSummaryBuilder.ValidateWindow(new TokenUsageQuery { FromUtc = _Anchor, ToUtc = _Anchor, BucketMinutes = 1e12 }), "bucket width ceiling");
                TokenUsageSummaryBuilder.ValidateWindow(new TokenUsageQuery { FromUtc = _Anchor.AddDays(-365), ToUtc = _Anchor, BucketMinutes = 1440 });
            }));

            cases.Add(Case("token_usage_without_window_bounds_the_record_span", "Token usage: without From/To the span of the records is bounded too", TestTags.Negative, () =>
            {
                List<TokenUsageRecord> records = new List<TokenUsageRecord>
                {
                    Record("m1", 10, _Anchor),
                    Record("m1", 10, _Anchor.AddMinutes(5000))
                };

                ThrowsLimit(() => TokenUsageSummaryBuilder.Build(records, new TokenUsageQuery { BucketMinutes = 1 }));
                TokenUsageSummaryResult ok = TokenUsageSummaryBuilder.Build(records, new TokenUsageQuery { BucketMinutes = 60 });
                AssertEqual(2, ok.Buckets.Count, "two populated buckets");
            }));

            cases.Add(Case("token_usage_models_beyond_the_limit_are_summed_into_other", "Token usage: ByModel and each bucket list the top ModelLimit models and OtherModels sums the rest exactly", TestTags.Positive, () =>
            {
                List<TokenUsageRecord> records = new List<TokenUsageRecord>();
                for (int m = 0; m < 12; m++)
                {
                    // m0 is the heaviest, m11 the lightest; every model appears in both buckets.
                    records.Add(Record("model-" + m.ToString("D2"), 1000 - m * 10, _Anchor.AddMinutes(1)));
                    records.Add(Record("model-" + m.ToString("D2"), 500 - m * 10, _Anchor.AddMinutes(20)));
                }

                TokenUsageQuery query = new TokenUsageQuery { FromUtc = _Anchor, ToUtc = _Anchor.AddMinutes(29), BucketMinutes = 15, ModelLimit = 3 };
                TokenUsageSummaryResult result = TokenUsageSummaryBuilder.Build(records, query);

                AssertEqual(3, result.ModelLimit, "limit echoed");
                AssertEqual(12, result.ModelCount, "distinct models");
                AssertEqual(3, result.ByModel.Count, "top models only");
                AssertEqual("model-00,model-01,model-02", String.Join(",", result.ByModel.Select(m => m.Model)), "most used first");
                AssertEqual(9, result.OtherModelCount, "the rest");
                AssertNotNull(result.OtherModels, "explicit other aggregate");
                AssertEqual(TokenUsageSummaryBuilder.OtherModelName, result.OtherModels!.Model, "other name");
                AssertEqual(result.TotalTokens, result.ByModel.Sum(m => m.TotalTokens) + result.OtherModels.TotalTokens, "nothing dropped");

                foreach (TokenUsageBucket bucket in result.Buckets)
                {
                    AssertTrue(bucket.Models.Count <= 3, "bucket lists at most the limit");
                    AssertTrue(bucket.Models.All(m => result.ByModel.Any(top => top.Model == m.Model)), "bucket breaks out the same top models");
                    long other = bucket.OtherModels != null ? bucket.OtherModels.TotalTokens : 0;
                    AssertEqual(bucket.TotalTokens, bucket.Models.Sum(m => m.TotalTokens) + other, "bucket total adds up");
                }

                TokenUsageSummaryResult wide = TokenUsageSummaryBuilder.Build(records, new TokenUsageQuery { FromUtc = query.FromUtc, ToUtc = query.ToUtc, BucketMinutes = 15, ModelLimit = 50 });
                AssertEqual(12, wide.ByModel.Count, "every model when under the limit");
                AssertNull(wide.OtherModels, "no other aggregate when every model is listed");
                AssertEqual(TokenUsageQuery.MaxModelLimit, new TokenUsageQuery { ModelLimit = 100000 }.ModelLimit, "limit normalized to the maximum");
                AssertEqual(1, new TokenUsageQuery { ModelLimit = 0 }.ModelLimit, "limit normalized to the minimum");
            }));

            cases.Add(Case("request_history_window_bounded", "Request history: 1000 buckets is accepted, 1001 rejected, and a width past 366 days is out of range", TestTags.Negative, () =>
            {
                RequestHistorySummaryResult result = RequestHistorySummaryBuilder.Build(new List<RequestHistoryEntry>(), new RequestHistoryQuery { FromUtc = _Anchor, ToUtc = _Anchor.AddMinutes(999), BucketMinutes = 1 });
                AssertEqual(CollectionLimits.MaxSummaryBuckets, result.Buckets.Count, "gap-filled to the limit");
                CollectionLimitExceededException ex = ThrowsLimit(() => RequestHistorySummaryBuilder.ValidateWindow(new RequestHistoryQuery { FromUtc = _Anchor, ToUtc = _Anchor.AddMinutes(1000), BucketMinutes = 1 }));
                AssertEqual("Buckets", ex.Field, "field");
                AssertThrows<ArgumentOutOfRangeException>(() => RequestHistorySummaryBuilder.ValidateWindow(new RequestHistoryQuery { FromUtc = _Anchor, ToUtc = _Anchor, BucketMinutes = int.MaxValue }), "bucket width ceiling");
            }));

            cases.Add(Case("collection_bounds_reject_delivery_lists_over_their_limits", "CollectionBounds rejects delivery, incident, runbook, settings, and push lists one over their declared limits", TestTags.Negative, () =>
            {
                AssertEqual("VoyageIds", ThrowsLimit(() => CollectionBounds.Validate(new ReleaseUpsertRequest { VoyageIds = Ids(CollectionLimits.MaxLinkedIds + 1) })).Field, "release voyages");
                CollectionBounds.Validate(new ReleaseUpsertRequest { VoyageIds = Ids(CollectionLimits.MaxLinkedIds) });
                AssertEqual("RescueMissionIds", ThrowsLimit(() => CollectionBounds.Validate(new Incident { RescueMissionIds = Ids(CollectionLimits.MaxLinkedIds + 1) })).Field, "rescue ids");
                AssertEqual("CheckRunIds", ThrowsLimit(() => CollectionBounds.Validate(new Deployment { CheckRunIds = Ids(CollectionLimits.MaxLinkedIds + 1) })).Field, "deployment checks");

                DeploymentVerificationDefinition definition = new DeploymentVerificationDefinition();
                for (int i = 0; i <= CollectionLimits.MaxConfigItems; i++) definition.Headers["X-H" + i] = "v";
                DeploymentEnvironment environment = new DeploymentEnvironment();
                environment.VerificationDefinitions.Add(definition);
                AssertEqual("VerificationDefinitions.Headers", ThrowsLimit(() => CollectionBounds.Validate(environment)).Field, "nested headers path");

                RunbookExecutionUpdateRequest update = new RunbookExecutionUpdateRequest { CompletedStepIds = Ids(CollectionLimits.MaxConfigItems + 1) };
                AssertEqual("CompletedStepIds", ThrowsLimit(() => CollectionBounds.Validate(update)).Field, "completed steps");

                Armada.Core.Settings.VesselImportSettings import = new Armada.Core.Settings.VesselImportSettings { AllowedRoots = Ids(CollectionLimits.MaxConfigItems + 1) };
                AssertEqual("AllowedRoots", ThrowsLimit(() => CollectionBounds.Validate(import)).Field, "settings allowed roots");
            }));

            cases.Add(Case("enum_lists_keep_one_entry_per_value", "ScoredCriteria and push categories drop duplicates and undefined values", TestTags.Positive, () =>
            {
                Armada.Core.Settings.RepositoryHealthSettings health = new Armada.Core.Settings.RepositoryHealthSettings();
                List<Armada.Core.Enums.VesselHealthCriterionEnum> repeated = new List<Armada.Core.Enums.VesselHealthCriterionEnum>();
                for (int i = 0; i < 500; i++) repeated.Add(Armada.Core.Enums.VesselHealthCriterionEnum.Branches);
                repeated.Add((Armada.Core.Enums.VesselHealthCriterionEnum)9999);
                health.ScoredCriteria = repeated;
                AssertEqual(1, health.ScoredCriteria.Count, "one entry per criterion");

                PushDevice device = new PushDevice();
                device.Categories = new List<Armada.Core.Enums.PushCategoryEnum> { Armada.Core.Enums.PushCategoryEnum.MissionFailed, Armada.Core.Enums.PushCategoryEnum.MissionFailed, (Armada.Core.Enums.PushCategoryEnum)77 };
                AssertEqual(1, device.Categories.Count, "one entry per category");
            }));

            return new TestSuiteDescriptor(SuiteId, "Summary and delivery collection bounds", cases);
        }

        #endregion

        #region Private-Methods

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

        private static TokenUsageRecord Record(string model, long total, DateTime created)
        {
            return new TokenUsageRecord
            {
                Model = model,
                InputTokens = total / 2,
                OutputTokens = total - total / 2,
                TotalTokens = total,
                CreatedUtc = created
            };
        }

        private static List<string> Ids(int count)
        {
            return Enumerable.Range(0, count).Select(i => "id_" + i).ToList();
        }

        private static CollectionLimitExceededException ThrowsLimit(Action action)
        {
            try
            {
                action();
            }
            catch (CollectionLimitExceededException ex)
            {
                return ex;
            }

            throw new InvalidOperationException("Expected CollectionLimitExceededException.");
        }

        #endregion
    }
}
