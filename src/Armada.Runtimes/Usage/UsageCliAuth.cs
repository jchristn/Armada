namespace Armada.Runtimes.Usage
{
    /// <summary>Authentication mode selected by the installed Gemini CLI.</summary>
    internal class UsageCliAuth
    {
        /// <summary>oauth-personal identifies Google login; other modes have no subscription probe.</summary>
        public string? SelectedType { get; set; }
    }
}
