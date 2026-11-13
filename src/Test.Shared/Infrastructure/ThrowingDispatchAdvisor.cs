namespace Test.Shared.Infrastructure
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// A dispatch advisor that always throws, to prove dispatch survives a failing advisor.
    /// </summary>
    public sealed class ThrowingDispatchAdvisor : IMissionDispatchAdvisor
    {
        /// <inheritdoc />
        public Task<MissionDispatchAdvice?> AdviseAsync(Mission mission, Vessel vessel, CancellationToken token = default)
        {
            throw new InvalidOperationException("advisor down");
        }
    }
}
