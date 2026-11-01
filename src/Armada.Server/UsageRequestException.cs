namespace Armada.Server
{
    using System;
    /// <summary>A capture rejection with a stable public status and retry boundary.</summary>
    public class UsageRequestException : InvalidOperationException
    {
        /// <summary>StatusCode: account usage contract.</summary>
        public int StatusCode { get; }
        /// <summary>NextEligibleUtc: account usage contract.</summary>
        public DateTime? NextEligibleUtc { get; }
        /// <summary>UsageRequestException: account usage contract.</summary>
        public UsageRequestException(int statusCode, string reason, DateTime? nextEligibleUtc = null) : base(reason)
        { StatusCode = statusCode; NextEligibleUtc = nextEligibleUtc; }
    }
}
