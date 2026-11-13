namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// A rule's proposed Armada change template (<see cref="BlackBoxRuleResilience"/>): a mission title, a one paragraph
    /// change and its acceptance test, with a base severity the advisor escalates by recurrence. Text fields are rule
    /// templates (<see cref="BlackBoxRuleTemplate"/>). Not thread-safe while being built; read-only afterwards.
    /// </summary>
    public class BlackBoxRuleChange
    {
        #region Public-Members

        /// <summary>Mission title (template).</summary>
        public string Title { get; set; } = "";

        /// <summary>One paragraph change (template).</summary>
        public string Change { get; set; } = "";

        /// <summary>Acceptance test (template), or null.</summary>
        public string? AcceptanceTest { get; set; } = null;

        /// <summary>Base severity before escalation by recurrence.</summary>
        public BlackBoxSeverityEnum Severity { get; set; } = BlackBoxSeverityEnum.Medium;

        /// <summary>Armada source locations the change touches (file:method).</summary>
        public List<string> CodePointers
        {
            get => _CodePointers;
            set => _CodePointers = value ?? new List<string>();
        }

        #endregion

        #region Private-Members

        private List<string> _CodePointers = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRuleChange()
        {
        }

        /// <summary>
        /// Build a change.
        /// </summary>
        /// <param name="title">Title template.</param>
        /// <param name="change">Change template.</param>
        /// <param name="acceptanceTest">Acceptance test template.</param>
        /// <param name="severity">Base severity.</param>
        /// <param name="codePointers">Code pointers.</param>
        /// <returns>The change.</returns>
        /// <exception cref="ArgumentNullException">Thrown when title or change is null or empty.</exception>
        public static BlackBoxRuleChange Of(string title, string change, string acceptanceTest, BlackBoxSeverityEnum severity, params string[] codePointers)
        {
            if (String.IsNullOrEmpty(title)) throw new ArgumentNullException(nameof(title));
            if (String.IsNullOrEmpty(change)) throw new ArgumentNullException(nameof(change));
            BlackBoxRuleChange c = new BlackBoxRuleChange { Title = title, Change = change, AcceptanceTest = acceptanceTest, Severity = severity };
            if (codePointers != null) c.CodePointers.AddRange(codePointers);
            return c;
        }

        #endregion
    }
}
