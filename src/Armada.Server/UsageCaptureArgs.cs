namespace Armada.Server
{
    /// <summary>Optional host disambiguation for a capture request.</summary>
    public class UsageCaptureArgs
    {
        /// <summary>CaptainId: account usage contract.</summary>
        public string? CaptainId { get; set; }
        /// <summary>HarborId: account usage contract.</summary>
        public string? HarborId { get; set; }
    }
}
