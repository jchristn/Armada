namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Models;

    /// <summary>Shared new-work eligibility guard, independent of runtime state.</summary>
    public static class CaptainEligibility
    {
        /// <summary>Reject a disabled captain before starting any new operation.</summary>
        public static void RequireActive(Captain captain)
        {
            ArgumentNullException.ThrowIfNull(captain);
            if (!captain.Active)
                throw new ArgumentException("Captain " + captain.Id + " is inactive. Enable Active before starting new work.");
        }
    }
}
