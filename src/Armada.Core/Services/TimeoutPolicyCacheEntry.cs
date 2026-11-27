namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Models;

    /// <summary>A model profile held by <see cref="TimeoutPolicyCache"/> and when it must be read again.</summary>
    internal sealed class TimeoutPolicyCacheEntry
    {
        internal ModelProfile? Profile { get; set; } = null;

        internal DateTime ExpiresUtc { get; set; } = DateTime.MinValue;
    }
}
