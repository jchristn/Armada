namespace Armada.Core.Services
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>An existing routing preference class, with its compatibility ordering preserved.</summary>
    internal sealed class CaptainRoutingBucket
    {
        internal string ReasonCode { get; set; } = "EligibleCaptain";
        internal List<Captain> Captains { get; set; } = new List<Captain>();
    }
}
