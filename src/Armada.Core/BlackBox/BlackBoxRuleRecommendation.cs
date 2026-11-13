namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// A rule's configuration recommendation template. Its id is the rule id in lower case, a dot, and
    /// <see cref="Key"/>. When <see cref="ApplyTool"/> is set, apply_blackbox_recommendation can apply it through that
    /// existing MCP tool (for example update_vessel with a landingMode patch). Not thread-safe while being built.
    /// </summary>
    public class BlackBoxRuleRecommendation
    {
        #region Public-Members

        /// <summary>Key, unique within the rule.</summary>
        public string Key { get; set; } = "";

        /// <summary>Setting name (matches the effective configuration's setting names).</summary>
        public string Setting { get; set; } = "";

        /// <summary>Where the setting is changed.</summary>
        public BlackBoxConfigSourceEnum Scope { get; set; } = BlackBoxConfigSourceEnum.Vessel;

        /// <summary>Suggested value (template), or null.</summary>
        public string? SuggestedValue { get; set; } = null;

        /// <summary>Rationale (template).</summary>
        public string? Rationale { get; set; } = null;

        /// <summary>How to apply it by hand (template).</summary>
        public string? HowToApply { get; set; } = null;

        /// <summary>MCP tool that applies it, or null when it is advice only.</summary>
        public string? ApplyTool { get; set; } = null;

        /// <summary>Arguments of the apply tool (templates).</summary>
        public Dictionary<string, string> ApplyArguments
        {
            get => _ApplyArguments;
            set => _ApplyArguments = value ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>Skip this recommendation when the current value already equals the suggested value.</summary>
        public bool SkipWhenCurrent { get; set; } = true;

        /// <summary>Optional: when its placeholders are missing it is left out instead of skipping the rule.</summary>
        public bool Optional { get; set; } = true;

        #endregion

        #region Private-Members

        private Dictionary<string, string> _ApplyArguments = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRuleRecommendation()
        {
        }

        #endregion
    }
}
