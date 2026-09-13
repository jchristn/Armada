namespace Armada.Core
{
    using System;

    /// <summary>
    /// Canonical built-in persona names and compatibility helpers.
    /// </summary>
    public static class PersonaCatalog
    {
        /// <summary>
        /// Worker persona name.
        /// </summary>
        public const string Worker = "Worker";

        /// <summary>
        /// Architect persona name.
        /// </summary>
        public const string Architect = "Architect";

        /// <summary>
        /// Product manager persona name.
        /// </summary>
        public const string ProductManager = "Product Manager";

        /// <summary>
        /// Usability engineer persona name.
        /// </summary>
        public const string UsabilityEngineer = "Usability Engineer";

        /// <summary>
        /// Canonical test engineer persona name.
        /// </summary>
        public const string TestEngineer = "Test Engineer";

        /// <summary>
        /// Legacy test engineer persona name used by older builds.
        /// </summary>
        public const string LegacyTestEngineer = "TestEngineer";

        /// <summary>
        /// Judge persona name.
        /// </summary>
        public const string Judge = "Judge";

        /// <summary>
        /// Linter persona name. Evaluates the mission's changed code and documentation for style and
        /// correctness, corrects clear in-scope violations, and reports what it found and fixed.
        /// </summary>
        public const string Linter = "Linter";

        /// <summary>
        /// Recorder persona name. Reviews a voyage's conversation and distills durable memories
        /// (episodic/semantic/procedural) into the vessel model context, the Armada memory store, and any
        /// external memory facilities.
        /// </summary>
        public const string Recorder = "Recorder";

        /// <summary>
        /// Normalize a persona name to the canonical built-in display name when applicable.
        /// </summary>
        /// <param name="persona">Persona name.</param>
        /// <returns>Canonical persona name, or the trimmed original name for unknown personas.</returns>
        public static string NormalizeName(string? persona)
        {
            if (String.IsNullOrWhiteSpace(persona)) return String.Empty;

            string trimmed = persona.Trim();
            if (String.Equals(trimmed, LegacyTestEngineer, StringComparison.OrdinalIgnoreCase))
                return TestEngineer;

            return trimmed;
        }

        /// <summary>
        /// Compare two persona names using canonical normalization.
        /// </summary>
        /// <param name="left">First persona name.</param>
        /// <param name="right">Second persona name.</param>
        /// <returns>True if both names refer to the same persona.</returns>
        public static bool Matches(string? left, string? right)
        {
            string normalizedLeft = NormalizeName(left);
            string normalizedRight = NormalizeName(right);

            if (String.IsNullOrEmpty(normalizedLeft) || String.IsNullOrEmpty(normalizedRight))
                return false;

            return String.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Replace legacy built-in persona references in free-form text.
        /// </summary>
        /// <param name="value">Input text.</param>
        /// <returns>Updated text, or null when the input was null.</returns>
        public static string? ReplaceLegacyTestEngineer(string? value)
        {
            if (value == null) return null;
            return value.Replace(LegacyTestEngineer, TestEngineer, StringComparison.Ordinal);
        }
    }
}
