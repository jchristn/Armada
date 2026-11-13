namespace Armada.Runtimes.Usage
{
    using System.Collections.Generic;
    /// <summary>Code Assist retrieveUserQuota response (Gemini CLI 0.66).</summary>
    public class GeminiQuotaResponse
    {
        /// <summary>Independent model/token-type buckets.</summary>
        public List<GeminiQuotaBucket>? Buckets { get; set; }
    }
}
