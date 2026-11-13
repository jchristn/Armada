namespace Armada.Runtimes.Usage
{
    /// <summary>Host-local CLI authentication selection. Commands are never executed by this reader.</summary>
    internal class UsageCliSettings
    {
        /// <summary>Claude's configured API-key helper, if any.</summary>
        public string? ApiKeyHelper { get; set; }
        /// <summary>Gemini security settings.</summary>
        public UsageCliSecurity? Security { get; set; }
    }
}
