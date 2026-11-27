namespace Armada.Helm.Commands
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// Persona update body that carries only the escalation ladder.
    /// </summary>
    public class PersonaLadderUpdate
    {
        /// <summary>
        /// Escalation ladder rungs in order; an empty list clears the ladder.
        /// </summary>
        public List<PersonaCaptainPriority> EscalationLadder { get; set; } = new List<PersonaCaptainPriority>();
    }
}
