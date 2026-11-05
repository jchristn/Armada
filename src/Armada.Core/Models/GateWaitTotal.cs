namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>Wait time attributed to one gate.</summary>
    public class GateWaitTotal
    {
        /// <summary>The gate.</summary>
        public MissionWaitGateEnum Gate { get; set; } = MissionWaitGateEnum.Other;

        /// <summary>Seconds waited on the gate within the window, summed across missions.</summary>
        public double Seconds { get; set; }

        /// <summary>Distinct missions that waited on the gate.</summary>
        public int Missions { get; set; }
    }
}
