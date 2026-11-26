namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>One broken hierarchy or ownership rule in a plan.</summary>
    public sealed class PlanFinding
    {
        /// <summary>The rule.</summary>
        public PlanFindingCodeEnum Code { get; set; } = PlanFindingCodeEnum.ParentNotEarlier;

        /// <summary>1-based position of the mission the finding is about.</summary>
        public int Position { get; set; } = 0;

        /// <summary>What is wrong and how to fix it.</summary>
        public string Message { get; set; } = "";

        /// <summary>The message.</summary>
        /// <returns>Message.</returns>
        public override string ToString() => Message;
    }
}
