namespace Armada.Core.Services
{
    using System;

    /// <summary>A slice description with its leading dependency reference removed.</summary>
    internal sealed class ArchitectDependencyExtractionResult
    {
        /// <summary>The description without the dependency line.</summary>
        internal string Description { get; set; } = String.Empty;

        /// <summary>The referenced slice, or null.</summary>
        internal string? DependsOnReference { get; set; } = null;
    }
}
