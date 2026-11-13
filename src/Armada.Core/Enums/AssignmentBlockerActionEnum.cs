namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>Navigation targets for a mission's next steps; configuration is still authorized by its own endpoint.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AssignmentBlockerActionEnum
    {
        /// <summary>Edit the holding vessel.</summary>
        EditVessel,
        /// <summary>Edit an endpoint-backed captain.</summary>
        EditCaptain,
        /// <summary>Add another captain.</summary>
        AddCaptain,
        /// <summary>Review deployment settings.</summary>
        EditSettings,
        /// <summary>Review inference endpoint configuration.</summary>
        EditEndpoint,
        /// <summary>Inspect a holding mission.</summary>
        OpenMission,
        /// <summary>Inspect a holding voyage.</summary>
        OpenVoyage
    }
}
