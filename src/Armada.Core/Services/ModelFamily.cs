namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// The model family of a captain (docs/MODEL_HETEROGENEITY.md, Evaluate (Full)): the leading name of its model with any
    /// provider prefix, size and version removed ("qwen3.8:27b" is qwen, "openai/gpt-oss-20b" is gpt), or for a vendor CLI
    /// without a model the vendor's family (ClaudeCode is claude, Codex is gpt, Gemini is gemini). Used to pick an evaluator
    /// that does not share the candidate's family.
    /// </summary>
    public static class ModelFamily
    {
        #region Private-Members

        private static readonly Dictionary<string, string> _Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "o", "gpt" },
            { "chatgpt", "gpt" },
            { "codex", "gpt" },
            { "openai", "gpt" },
            { "sonnet", "claude" },
            { "opus", "claude" },
            { "haiku", "claude" },
            { "anthropic", "claude" },
            { "meta", "llama" },
            { "codellama", "llama" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// The family of a captain's model.
        /// </summary>
        /// <param name="captain">Captain.</param>
        /// <param name="endpoint">Its model endpoint, or null.</param>
        /// <returns>The family in lower case; never empty.</returns>
        /// <exception cref="ArgumentNullException">Thrown when captain is null.</exception>
        public static string Of(Captain captain, ModelEndpoint? endpoint)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            string? model = !String.IsNullOrWhiteSpace(captain.Model) ? captain.Model : endpoint?.Model;
            string? family = FromModel(model);
            return family ?? FromRuntime(captain.Runtime);
        }

        /// <summary>
        /// The family named by a model string, or null when it has no leading letters.
        /// </summary>
        /// <param name="model">Model name, or null.</param>
        /// <returns>The family in lower case, or null.</returns>
        public static string? FromModel(string? model)
        {
            if (String.IsNullOrWhiteSpace(model)) return null;
            string name = model.Trim();
            int slash = name.LastIndexOf('/');
            if (slash >= 0 && slash < name.Length - 1) name = name.Substring(slash + 1);

            StringBuilder letters = new StringBuilder();
            foreach (char c in name)
            {
                if (c >= 'a' && c <= 'z') letters.Append(c);
                else if (c >= 'A' && c <= 'Z') letters.Append(Char.ToLowerInvariant(c));
                else break;
            }

            if (letters.Length == 0) return null;
            string family = letters.ToString();
            return _Aliases.TryGetValue(family, out string? alias) ? alias : family;
        }

        /// <summary>
        /// The family a vendor runtime uses when the captain names no model.
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <returns>The family in lower case.</returns>
        public static string FromRuntime(AgentRuntimeEnum runtime)
        {
            switch (runtime)
            {
                case AgentRuntimeEnum.ClaudeCode: return "claude";
                case AgentRuntimeEnum.Codex: return "gpt";
                case AgentRuntimeEnum.Gemini: return "gemini";
                default: return "runtime:" + runtime.ToString().ToLowerInvariant();
            }
        }

        #endregion
    }
}
