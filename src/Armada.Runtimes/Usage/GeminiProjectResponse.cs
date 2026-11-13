namespace Armada.Runtimes.Usage
{
    /// <summary>Read-only loadCodeAssist response projection, Gemini CLI 0.66.</summary>
    internal class GeminiProjectResponse
    {
        /// <summary>Existing project resolved by the authenticated CLI account. Never onboard or create one.</summary>
        public string? CloudaicompanionProject { get; set; }
    }
}
