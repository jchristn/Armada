namespace Armada.Runtimes.Usage
{
    /// <summary>Gemini's host-local security settings projection.</summary>
    internal class UsageCliSecurity
    {
        /// <summary>Active authentication selection.</summary>
        public UsageCliAuth? Auth { get; set; }
    }
}
