namespace Armada.Core.Services
{
    using System;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Removes model reasoning that leaked into reply text. Open models served through a chat template that opens the
    /// think block in the prompt (Qwen3, DeepSeek-R1) emit only the closing tag, so a reply can begin with reasoning and a
    /// bare <c>&lt;/think&gt;</c>; others leave a complete <c>&lt;think&gt;...&lt;/think&gt;</c> block, or an unclosed
    /// one when the output was cut off. Text that quotes a tag inside backticks is left alone.
    /// </summary>
    public static class ReasoningText
    {
        #region Private-Members

        // A complete think block is <think> or <think ...> through </think>. The name must end at the tag boundary, so a
        // longer name (<thinking>) stays in the text.
        private static readonly Regex _CompleteBlock = new Regex(
            @"<think(?:\s[^>]*)?>.*?</think\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // A closing tag that ends its line and is not quoted in backticks.
        private static readonly Regex _BareClose = new Regex(
            @"(?<!`)</think\s*>(?!`)[ \t]*(?:\r?\n|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex _LeadingOpen = new Regex(
            @"\A\s*<think(?:\s[^>]*)?>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex _AnyOpen = new Regex(
            @"<think(?:\s[^>]*)?>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Remove leaked reasoning from reply text: every complete think block, then the text before a bare closing tag
        /// when no opening tag precedes it, then an unclosed think block that starts the text (the rest of the text is
        /// reasoning). Text without reasoning is returned unchanged; text that was changed is trimmed.
        /// </summary>
        /// <param name="text">The reply text, or null.</param>
        /// <returns>The text without reasoning; an empty string for null.</returns>
        public static string Strip(string? text)
        {
            if (String.IsNullOrEmpty(text)) return String.Empty;
            if (text.IndexOf("think", StringComparison.OrdinalIgnoreCase) < 0) return text;

            string result = _CompleteBlock.Replace(text, String.Empty);

            Match close = _BareClose.Match(result);
            if (close.Success && !_AnyOpen.IsMatch(result.Substring(0, close.Index)))
                result = result.Substring(close.Index + close.Length);

            if (_LeadingOpen.IsMatch(result)) result = String.Empty;

            return String.Equals(result, text, StringComparison.Ordinal) ? text : result.Trim();
        }

        /// <summary>
        /// Whether <see cref="Strip"/> would change the text.
        /// </summary>
        /// <param name="text">The reply text, or null.</param>
        /// <returns>True when the text carries leaked reasoning.</returns>
        public static bool HasReasoning(string? text)
        {
            if (String.IsNullOrEmpty(text)) return false;
            return !String.Equals(Strip(text), text, StringComparison.Ordinal);
        }

        #endregion
    }
}
