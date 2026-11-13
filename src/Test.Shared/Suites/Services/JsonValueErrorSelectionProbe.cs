namespace Test.Shared.Suites.Services
{
    using Armada.Core.Enums;

    /// <summary>Nested group of <see cref="JsonValueErrorProbe"/> with a string enum like the real settings.</summary>
    public sealed class JsonValueErrorSelectionProbe
    {
        /// <summary>Enum member.</summary>
        public UsageSelectionModeEnum Mode { get; set; }
    }
}
