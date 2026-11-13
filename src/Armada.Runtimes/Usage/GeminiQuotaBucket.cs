namespace Armada.Runtimes.Usage
{
    /// <summary>Read-only Code Assist model quota bucket.</summary>
    public class GeminiQuotaBucket
    {
        /// <summary>Provider model ID.</summary>
        public string? ModelId { get; set; }
        /// <summary>Counter unit, when reported.</summary>
        public string? TokenType { get; set; }
        /// <summary>Remaining fraction; absence is unknown.</summary>
        public double? RemainingFraction { get; set; }
        /// <summary>Absolute reset timestamp.</summary>
        public string? ResetTime { get; set; }
    }
}
