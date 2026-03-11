namespace Armada.Core.Services.Interfaces
{
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// Renders message templates with placeholder substitution for commit messages and PR descriptions.
    /// </summary>
    public interface IMessageTemplateService
    {
        /// <summary>
        /// Replace placeholder parameters in a template string.
        /// </summary>
        /// <param name="template">Template string with {Placeholder} tokens.</param>
        /// <param name="parameters">Key-value pairs for substitution.</param>
        /// <returns>Rendered string.</returns>
        string RenderTemplate(string template, Dictionary<string, string> parameters);

        /// <summary>
        /// Build a context dictionary from domain objects.
        /// </summary>
        /// <param name="mission">Mission (required).</param>
        /// <param name="captain">Captain (optional).</param>
        /// <param name="vessel">Vessel (optional).</param>
        /// <param name="voyage">Voyage (optional).</param>
        /// <param name="dock">Dock (optional).</param>
        /// <returns>Dictionary of placeholder keys and values.</returns>
        Dictionary<string, string> BuildContext(Mission mission, Captain? captain = null, Vessel? vessel = null, Voyage? voyage = null, Dock? dock = null);

        /// <summary>
        /// Render commit message instructions for injection into an agent prompt.
        /// Returns empty string if commit metadata is disabled.
        /// </summary>
        /// <param name="settings">Message template settings.</param>
        /// <param name="context">Placeholder context dictionary.</param>
        /// <returns>Agent-friendly instruction text, or empty string.</returns>
        string RenderCommitInstructions(MessageTemplateSettings settings, Dictionary<string, string> context);

        /// <summary>
        /// Render a PR description by appending template metadata to the base body.
        /// Returns the base body unchanged if PR metadata is disabled.
        /// </summary>
        /// <param name="settings">Message template settings.</param>
        /// <param name="baseBody">Original PR body text.</param>
        /// <param name="context">Placeholder context dictionary.</param>
        /// <returns>PR body with metadata appended.</returns>
        string RenderPrDescription(MessageTemplateSettings settings, string baseBody, Dictionary<string, string> context);

        /// <summary>
        /// Render a merge commit message from the template.
        /// Returns null if commit metadata is disabled.
        /// </summary>
        /// <param name="settings">Message template settings.</param>
        /// <param name="context">Placeholder context dictionary.</param>
        /// <returns>Merge commit message, or null.</returns>
        string? RenderMergeCommitMessage(MessageTemplateSettings settings, Dictionary<string, string> context);
    }
}
