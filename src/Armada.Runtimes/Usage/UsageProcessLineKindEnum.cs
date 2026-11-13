namespace Armada.Runtimes.Usage
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Outcome of reading one line from a <see cref="UsageProcessSession"/>.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UsageProcessLineKindEnum
    {
        /// <summary>
        /// A complete line was read.
        /// </summary>
        [EnumMember(Value = "Line")]
        Line,

        /// <summary>
        /// The helper closed stdout at a line boundary.
        /// </summary>
        [EnumMember(Value = "EndOfStream")]
        EndOfStream,

        /// <summary>
        /// The helper closed stdout in the middle of a line; the partial line is discarded.
        /// </summary>
        [EnumMember(Value = "Truncated")]
        Truncated,

        /// <summary>
        /// A line or the whole session exceeded its byte cap; nothing past the cap is parsed.
        /// </summary>
        [EnumMember(Value = "Oversize")]
        Oversize
    }
}
