namespace Test.Shared.Infrastructure.CrashPoints
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// How an operation ended when run with an armed crash point.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CrashRunOutcomeEnum
    {
        /// <summary>The armed point fired; the operation is frozen there, as if the process had died.</summary>
        Crashed,

        /// <summary>The operation finished without reaching the armed point.</summary>
        NotReached
    }
}
