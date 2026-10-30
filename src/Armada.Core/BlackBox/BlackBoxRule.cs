namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// One black box advisor rule (<see cref="BlackBoxRuleCatalog"/>): an id such as <c>BBX-LAND-001</c>, a match
    /// predicate over the record, a why statement (template) with confidence and evidence references, a triage vote, and
    /// optional configuration recommendations, Armada improvement hypotheses and recovery paths. When the why statement or
    /// a non-optional component has a placeholder the record cannot fill, the rule is skipped and the advisor logs it.
    /// Built with the fluent methods; not thread-safe while being built, read-only afterwards.
    /// </summary>
    public class BlackBoxRule
    {
        #region Public-Members

        /// <summary>Rule id (for example BBX-LAND-001).</summary>
        public string Id { get; }

        /// <summary>Short title.</summary>
        public string Title { get; }

        /// <summary>The phase the rule is about (documentation and coverage tests).</summary>
        public BlackBoxPhaseEnum Phase { get; set; } = BlackBoxPhaseEnum.Unknown;

        /// <summary>Match predicate. Exceptions are treated as no match.</summary>
        public Func<BlackBoxRecord, bool> Match { get; set; }

        /// <summary>Why statement (template).</summary>
        public string Why { get; set; } = "";

        /// <summary>Confidence of the why statement.</summary>
        public BlackBoxConfidenceEnum Confidence { get; set; } = BlackBoxConfidenceEnum.Medium;

        /// <summary>Evidence references (record paths such as workspace.dirtyPaths, or object names).</summary>
        public List<string> EvidenceRefs { get; } = new List<string>();

        /// <summary>Triage vote (Unknown means no vote).</summary>
        public BlackBoxTriageEnum Vote { get; set; } = BlackBoxTriageEnum.Unknown;

        /// <summary>Weight of the vote (0 means no vote).</summary>
        public int Weight { get; set; } = 0;

        /// <summary>Rule ids that, when they also match, make this rule step aside (a generic rule yields to a specific one).</summary>
        public List<string> SuppressedBy { get; } = new List<string>();

        /// <summary>Configuration recommendations.</summary>
        public List<BlackBoxRuleRecommendation> Recommendations { get; } = new List<BlackBoxRuleRecommendation>();

        /// <summary>Armada improvement hypotheses.</summary>
        public List<BlackBoxRuleImprovement> Improvements { get; } = new List<BlackBoxRuleImprovement>();

        /// <summary>Recovery paths.</summary>
        public List<BlackBoxRulePath> Paths { get; } = new List<BlackBoxRulePath>();

        /// <summary>The fallback rule fires only when no other rule matched.</summary>
        public bool IsFallback { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="id">Rule id.</param>
        /// <param name="title">Title.</param>
        /// <param name="match">Match predicate.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null or empty.</exception>
        public BlackBoxRule(string id, string title, Func<BlackBoxRecord, bool> match)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            if (String.IsNullOrEmpty(title)) throw new ArgumentNullException(nameof(title));
            Id = id;
            Title = title;
            Match = match ?? throw new ArgumentNullException(nameof(match));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The stable id of a component of this rule: the rule id in lower case, a dot and the key.
        /// </summary>
        /// <param name="key">Component key.</param>
        /// <returns>The id.</returns>
        public string ComponentId(string key)
        {
            return Id.ToLowerInvariant() + "." + key;
        }

        /// <summary>Set the why statement.</summary>
        /// <param name="why">Template.</param>
        /// <param name="confidence">Confidence.</param>
        /// <param name="evidence">Evidence references.</param>
        /// <returns>This rule.</returns>
        public BlackBoxRule Because(string why, BlackBoxConfidenceEnum confidence, params string[] evidence)
        {
            Why = why ?? throw new ArgumentNullException(nameof(why));
            Confidence = confidence;
            EvidenceRefs.AddRange(evidence);
            return this;
        }

        /// <summary>Set the triage vote.</summary>
        /// <param name="verdict">Verdict.</param>
        /// <param name="weight">Weight (0 for no vote).</param>
        /// <returns>This rule.</returns>
        public BlackBoxRule Votes(BlackBoxTriageEnum verdict, int weight)
        {
            Vote = verdict;
            Weight = Math.Max(0, weight);
            return this;
        }

        /// <summary>Set the phase.</summary>
        /// <param name="phase">Phase.</param>
        /// <returns>This rule.</returns>
        public BlackBoxRule In(BlackBoxPhaseEnum phase)
        {
            Phase = phase;
            return this;
        }

        /// <summary>Step aside when any of these rules also match.</summary>
        /// <param name="ruleIds">Rule ids.</param>
        /// <returns>This rule.</returns>
        public BlackBoxRule YieldsTo(params string[] ruleIds)
        {
            SuppressedBy.AddRange(ruleIds);
            return this;
        }

        /// <summary>Add a configuration recommendation.</summary>
        /// <param name="recommendation">Recommendation.</param>
        /// <returns>This rule.</returns>
        public BlackBoxRule Recommend(BlackBoxRuleRecommendation recommendation)
        {
            Recommendations.Add(recommendation ?? throw new ArgumentNullException(nameof(recommendation)));
            return this;
        }

        /// <summary>Add an improvement hypothesis.</summary>
        /// <param name="improvement">Improvement.</param>
        /// <returns>This rule.</returns>
        public BlackBoxRule Improve(BlackBoxRuleImprovement improvement)
        {
            Improvements.Add(improvement ?? throw new ArgumentNullException(nameof(improvement)));
            return this;
        }

        /// <summary>Add a recovery path.</summary>
        /// <param name="path">Path.</param>
        /// <returns>This rule.</returns>
        public BlackBoxRule Recover(BlackBoxRulePath path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (path.Priority == 0) path.Priority = Paths.Count + 1;
            Paths.Add(path);
            return this;
        }

        #endregion
    }
}
