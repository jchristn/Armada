namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// One contributed setting (only its type is read).
    /// </summary>
    public sealed class VsCodeSettingContribution
    {
        /// <summary>JSON schema type.</summary>
        public string Type { get; set; } = "";
    }
}
