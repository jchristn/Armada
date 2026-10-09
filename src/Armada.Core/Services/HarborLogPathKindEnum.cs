namespace Armada.Core.Services
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// What the directory a Harbor log entry names is, so the activity log can shorten it.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborLogPathKindEnum
    {
        /// <summary>
        /// No directory.
        /// </summary>
        [EnumMember(Value = "None")]
        None,

        /// <summary>
        /// A mission dock (shown as vessel and short mission ID).
        /// </summary>
        [EnumMember(Value = "Dock")]
        Dock,

        /// <summary>
        /// A vessel's checkout or repository on this host.
        /// </summary>
        [EnumMember(Value = "Checkout")]
        Checkout,

        /// <summary>
        /// A per-job scratch directory Harbor created (shown as scratch).
        /// </summary>
        [EnumMember(Value = "Scratch")]
        Scratch,

        /// <summary>
        /// Any other directory.
        /// </summary>
        [EnumMember(Value = "Other")]
        Other
    }
}
