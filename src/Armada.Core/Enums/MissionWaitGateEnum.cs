namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;
    /// <summary>Scheduling gates that can prevent a mission from advancing.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MissionWaitGateEnum
    {
        /// <summary>A prerequisite mission has not completed.</summary>
        Dependency,
        /// <summary>The parent mission prevents progress.</summary>
        Parent,
        /// <summary>An integration barrier is held.</summary>
        IntegrationBarrier,
        /// <summary>A broad scope conflicts with another mission.</summary>
        BroadScope,
        /// <summary>The branch is held by another mission.</summary>
        BranchLock,
        /// <summary>The vessel has reached its voyage limit.</summary>
        VesselVoyageSlot,
        /// <summary>The voyage has reached its mission limit.</summary>
        VoyageMissionSlot,
        /// <summary>No eligible captain slot is available.</summary>
        CaptainSlot,
        /// <summary>The endpoint has reached its limit.</summary>
        EndpointSlot,
        /// <summary>The runtime has reached its limit.</summary>
        RuntimeSlot,
        /// <summary>The global dispatch limit is reached.</summary>
        GlobalSlot,
        /// <summary>No eligible execution host is available.</summary>
        Placement,
        /// <summary>A launch retry is backing off.</summary>
        LaunchBackoff,
        /// <summary>Another scheduling gate prevents progress.</summary>
        Other,
    }
}
