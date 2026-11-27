namespace Armada.Server.Mcp
{
    using Armada.Core.Models;

    /// <summary>
    /// Arguments of update_model_profile: the profile id plus the sections of <see cref="ModelProfileUpdateRequest"/>.
    /// </summary>
    public class ModelProfileUpdateArgs : ModelProfileUpdateRequest
    {
        /// <summary>
        /// Profile (mpf_ prefix).
        /// </summary>
        public string? Id { get; set; } = null;
    }
}
