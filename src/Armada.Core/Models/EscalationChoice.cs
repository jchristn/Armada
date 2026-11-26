namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// The captain chosen for one escalation hop and why (RS-5.6): a rung of the persona's escalation ladder, or (with no
    /// ladder) a strictly higher effective tier.
    /// </summary>
    public class EscalationChoice
    {
        /// <summary>The captain the mission escalates to.</summary>
        public Captain Captain { get; }

        /// <summary>Zero-based ladder rung the captain matched, or null when the hop used the higher-tier rule.</summary>
        public int? Rung { get; }

        /// <summary>Rungs on the ladder, or 0 when the hop used the higher-tier rule.</summary>
        public int LadderLength { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="captain">The chosen captain.</param>
        /// <param name="rung">The matched rung, or null for the higher-tier rule.</param>
        /// <param name="ladderLength">Rungs on the ladder, or 0.</param>
        /// <exception cref="ArgumentNullException">Thrown when captain is null.</exception>
        public EscalationChoice(Captain captain, int? rung, int ladderLength)
        {
            Captain = captain ?? throw new ArgumentNullException(nameof(captain));
            Rung = rung;
            LadderLength = ladderLength;
        }

        /// <summary>
        /// A short description for notes and logs, for example "rung 2 of 3" or "a higher tier".
        /// </summary>
        /// <returns>The description.</returns>
        public string Describe()
        {
            return Rung.HasValue ? "escalation ladder rung " + (Rung.Value + 1) + " of " + LadderLength : "a captain with a higher tier";
        }
    }
}
