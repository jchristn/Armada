namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Why Armada found no place to work in a vessel's checkout: the Admiral has no usable working directory for it, and
    /// no connected Harbor can serve it.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum VesselCheckoutErrorCodeEnum
    {
        /// <summary>
        /// No Harbor the request may use is connected (or none of the connected ones serves checkout operations).
        /// </summary>
        [EnumMember(Value = "NoHarborConnected")]
        NoHarborConnected,

        /// <summary>
        /// Harbors are connected, but none has a checkout of the vessel.
        /// </summary>
        [EnumMember(Value = "NoHarborCheckout")]
        NoHarborCheckout
    }
}
