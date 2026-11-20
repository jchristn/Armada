namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;

    /// <summary>
    /// The contributed configuration section.
    /// </summary>
    public sealed class VsCodeConfigurationContribution
    {
        /// <summary>Title.</summary>
        public string Title { get; set; } = "";

        /// <summary>Settings by key.</summary>
        public Dictionary<string, VsCodeSettingContribution> Properties { get; set; } = new Dictionary<string, VsCodeSettingContribution>();
    }
}
