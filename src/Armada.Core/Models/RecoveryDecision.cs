namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// What <see cref="Armada.Core.Services.RecoveryPolicyEngine"/> decided (RS-5.10): the actions to try, in order. The
    /// caller tries each one and stops at the first it can carry out; an action it cannot carry out (no stronger captain,
    /// a lost race) falls through to the next. The last step is always Fail, Wait or an action that cannot fall through.
    /// </summary>
    public class RecoveryDecision
    {
        /// <summary>The actions to try, in order.</summary>
        [CollectionBound(16, Reason = "At most one per RecoveryActionEnum value")]
        public List<RecoveryStep> Steps { get; set; } = new List<RecoveryStep>();

        /// <summary>The first action to try.</summary>
        public RecoveryStep First => Steps.Count > 0 ? Steps[0] : new RecoveryStep(RecoveryActionEnum.Fail);

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RecoveryDecision()
        {
        }

        /// <summary>
        /// Report whether the decision includes an action.
        /// </summary>
        /// <param name="action">The action.</param>
        /// <returns>True when a step has that action.</returns>
        public bool Includes(RecoveryActionEnum action)
        {
            return Steps.Any(s => s.Action == action);
        }

        /// <summary>
        /// The steps with an action, in order.
        /// </summary>
        /// <param name="action">The action.</param>
        /// <returns>Matching steps.</returns>
        public List<RecoveryStep> StepsFor(RecoveryActionEnum action)
        {
            return Steps.Where(s => s.Action == action).ToList();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return String.Join(", then ", Steps.Select(s => s.ToString()));
        }
    }
}
