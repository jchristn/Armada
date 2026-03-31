namespace Armada.Core.Services.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Service for resolving and rendering prompt templates.
    /// Supports database-stored overrides with embedded resource defaults as fallback.
    /// </summary>
    public interface IPromptTemplateService
    {
        /// <summary>
        /// Resolve a template by name. Checks database first, falls back to embedded resource.
        /// </summary>
        /// <param name="name">Template name (e.g. "mission.rules", "persona.worker").</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Resolved template, or null if not found.</returns>
        Task<PromptTemplate?> ResolveAsync(string name, CancellationToken token = default);

        /// <summary>
        /// Render a template by name with placeholder substitution.
        /// </summary>
        /// <param name="name">Template name.</param>
        /// <param name="parameters">Key-value pairs for {Placeholder} substitution.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rendered string, or empty string if template not found.</returns>
        Task<string> RenderAsync(string name, Dictionary<string, string> parameters, CancellationToken token = default);

        /// <summary>
        /// Seed all built-in templates into the database if they don't already exist.
        /// Called on startup.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        Task SeedDefaultsAsync(CancellationToken token = default);

        /// <summary>
        /// List all templates, optionally filtered by category.
        /// </summary>
        /// <param name="category">Optional category filter (e.g. "mission", "persona").</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of matching templates.</returns>
        Task<List<PromptTemplate>> ListAsync(string? category = null, CancellationToken token = default);

        /// <summary>
        /// Reset a template to its embedded resource default content.
        /// </summary>
        /// <param name="name">Template name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated template, or null if no embedded default exists for the name.</returns>
        Task<PromptTemplate?> ResetToDefaultAsync(string name, CancellationToken token = default);

        /// <summary>
        /// Get the embedded resource default content for a template by name.
        /// Returns null if no embedded default exists.
        /// </summary>
        /// <param name="name">Template name.</param>
        /// <returns>Default content string, or null.</returns>
        string? GetEmbeddedDefault(string name);
    }
}
