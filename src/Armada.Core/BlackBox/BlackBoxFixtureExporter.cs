namespace Armada.Core.BlackBox
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Builds black box regression fixtures (plan 13.2 item 8): a sanitized copy of a record (secrets redacted, tenant and
    /// user ids removed, repository URL replaced, absolute host paths shortened to their last two segments, similar
    /// entries, notes and the analysis dropped) plus the rule ids and triage the advisor produces for it now, so a fixture
    /// suite can check that later rule versions still produce them. Pure except <see cref="WriteAsync"/>. Thread-safe.
    /// </summary>
    public static class BlackBoxFixtureExporter
    {
        #region Private-Members

        private static readonly Regex _AbsolutePath = new Regex(@"(?:[A-Za-z]:\\\\|/)(?:Users|home|private|tmp|var|opt|srv|mnt|Volumes|root|data|workspace)(?:[/\\]{1,2}[^\s""'`)\]\\/,;]+)+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Name = new Regex("[^a-z0-9_-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// A sanitized deep copy of a record.
        /// </summary>
        /// <param name="record">Record (not modified).</param>
        /// <param name="redactor">Redactor, or null for a default one.</param>
        /// <returns>The sanitized copy.</returns>
        /// <exception cref="ArgumentNullException">Thrown when record is null.</exception>
        public static BlackBoxRecord Sanitize(BlackBoxRecord record, BlackBoxRedactor? redactor = null)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            string json = JsonSerializer.Serialize(record, BlackBoxJson.CompactOptions);
            json = _AbsolutePath.Replace(json, m => ShortPath(m.Value));
            BlackBoxRecord copy = JsonSerializer.Deserialize<BlackBoxRecord>(json, BlackBoxJson.Options) ?? new BlackBoxRecord();
            (redactor ?? new BlackBoxRedactor()).RedactRecord(copy);
            if (copy.Subject != null)
            {
                copy.Subject.TenantId = null;
                copy.Subject.UserId = null;
                if (!String.IsNullOrEmpty(copy.Subject.VesselRepoUrl)) copy.Subject.VesselRepoUrl = "https://example.invalid/" + Slug(copy.Subject.VesselName ?? "vessel") + ".git";
            }
            if (copy.Related != null)
            {
                copy.Related.SimilarEntries.Clear();
                copy.Related.OccurrencesLast30Days = 0;
            }
            copy.Analysis = null;
            return copy;
        }

        /// <summary>
        /// Build a fixture from a record: sanitize it, analyze the sanitized copy and record the rule ids and triage.
        /// </summary>
        /// <param name="record">Record.</param>
        /// <param name="advisor">Advisor whose current output becomes the expectation.</param>
        /// <param name="name">Fixture name, or null to derive one from the subject.</param>
        /// <param name="description">Description, or null.</param>
        /// <param name="source">Source (entry or mission id), or null.</param>
        /// <param name="redactor">Redactor, or null.</param>
        /// <returns>The fixture.</returns>
        /// <exception cref="ArgumentNullException">Thrown when record or advisor is null.</exception>
        public static BlackBoxFixture Build(BlackBoxRecord record, IBlackBoxAdvisor advisor, string? name = null, string? description = null, string? source = null, BlackBoxRedactor? redactor = null)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (advisor == null) throw new ArgumentNullException(nameof(advisor));
            BlackBoxRecord sanitized = Sanitize(record, redactor);
            BlackBoxAnalysis? analysis = advisor.Analyze(sanitized);
            string derived = name ?? (sanitized.Subject?.MissionId ?? sanitized.Subject?.VoyageId ?? sanitized.EntryId);
            return new BlackBoxFixture
            {
                Name = Slug(derived),
                Description = description ?? ("Black box fixture for " + (sanitized.Site?.Phase.ToString() ?? "Unknown") + " / " + (sanitized.Site?.Step.ToString() ?? "Unknown") + "."),
                Source = source ?? sanitized.EntryId,
                RulesVersion = advisor.RulesVersion,
                ExpectedRuleIds = analysis == null ? new System.Collections.Generic.List<string>() : analysis.Why.Select(w => w.RuleId).Distinct(StringComparer.Ordinal).ToList(),
                ExpectedTriage = analysis?.Triage?.Verdict ?? Armada.Core.Enums.BlackBoxTriageEnum.Unknown,
                ExpectedResilience = analysis?.Resilience == null ? null : new BlackBoxFixtureResilience
                {
                    InScope = analysis.Resilience.InScope,
                    SelfHealable = analysis.Resilience.SelfHealable
                },
                Record = sanitized
            };
        }

        /// <summary>
        /// Serialize a fixture as indented camelCase JSON.
        /// </summary>
        /// <param name="fixture">Fixture.</param>
        /// <returns>The JSON text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when fixture is null.</exception>
        public static string Serialize(BlackBoxFixture fixture)
        {
            if (fixture == null) throw new ArgumentNullException(nameof(fixture));
            return JsonSerializer.Serialize(fixture, BlackBoxJson.Options) + "\n";
        }

        /// <summary>
        /// Parse a fixture.
        /// </summary>
        /// <param name="json">JSON text.</param>
        /// <returns>The fixture.</returns>
        /// <exception cref="ArgumentNullException">Thrown when json is null.</exception>
        /// <exception cref="JsonException">Thrown when the text is not a fixture.</exception>
        public static BlackBoxFixture Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            return JsonSerializer.Deserialize<BlackBoxFixture>(json, BlackBoxJson.Options) ?? throw new JsonException("The fixture is empty.");
        }

        /// <summary>
        /// Write a fixture to <c>{directory}/{name}.json</c> (for example src/Test.Shared/Fixtures/BlackBox).
        /// </summary>
        /// <param name="fixture">Fixture.</param>
        /// <param name="directory">Directory (created when missing).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The file path.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null or empty.</exception>
        public static async Task<string> WriteAsync(BlackBoxFixture fixture, string directory, CancellationToken token = default)
        {
            if (fixture == null) throw new ArgumentNullException(nameof(fixture));
            if (String.IsNullOrWhiteSpace(directory)) throw new ArgumentNullException(nameof(directory));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, Slug(fixture.Name) + ".json");
            await File.WriteAllTextAsync(path, Serialize(fixture), new UTF8Encoding(false), token).ConfigureAwait(false);
            return path;
        }

        #endregion

        #region Private-Methods

        private static string ShortPath(string path)
        {
            string[] parts = path.Replace("\\\\", "/").Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            return "/sanitized/" + String.Join("/", parts.Skip(Math.Max(0, parts.Length - 2)));
        }

        private static string Slug(string value)
        {
            string slug = _Name.Replace((value ?? "fixture").ToLowerInvariant(), "-").Trim('-');
            return slug.Length == 0 ? "fixture" : (slug.Length > 80 ? slug.Substring(0, 80) : slug);
        }

        #endregion
    }
}
