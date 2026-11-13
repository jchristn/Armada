namespace Armada.Core.Models
{
    using Armada.Core.Services;

    /// <summary>A named, validated runtime value for an action template.</summary>
    public class FleetActionInput
    {
        /// <summary>Template name, using letters, digits and underscores.</summary>
        public string Name { get; set; } = "";
        /// <summary>string, semver, enum, or package (a shell-safe package identifier such as Newtonsoft.Json or @scope/name).</summary>
        public string Type { get; set; } = "string";
        /// <summary>Whether a nonempty value is required.</summary>
        public bool Required { get; set; }
        /// <summary>Default value when omitted.</summary>
        public string? Default { get; set; }
        /// <summary>Permitted enum values.</summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public System.Collections.Generic.List<string> AllowedValues { get => _AllowedValues; set => _AllowedValues = value ?? new(); }
        private System.Collections.Generic.List<string> _AllowedValues = new();
        /// <summary>Help text for the run dialog.</summary>
        public string? Description { get; set; }
    }
}
