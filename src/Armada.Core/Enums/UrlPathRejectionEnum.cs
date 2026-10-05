namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Why a URL path could not be canonicalized by <see cref="Armada.Core.UrlPathCanonicalizer"/>.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UrlPathRejectionEnum
    {
        /// <summary>
        /// The path was accepted.
        /// </summary>
        [EnumMember(Value = "None")]
        None,

        /// <summary>
        /// The path was null, empty, or whitespace.
        /// </summary>
        [EnumMember(Value = "Empty")]
        Empty,

        /// <summary>
        /// The path contains a character that is never valid in a canonical path (control character, backslash,
        /// semicolon, query or fragment delimiter).
        /// </summary>
        [EnumMember(Value = "InvalidCharacter")]
        InvalidCharacter,

        /// <summary>
        /// A percent sign is not followed by two hexadecimal digits, or the decoded bytes are not valid UTF-8.
        /// </summary>
        [EnumMember(Value = "MalformedEncoding")]
        MalformedEncoding,

        /// <summary>
        /// A percent-encoded character could change how the path splits or resolves (slash, backslash, dot,
        /// percent, semicolon, or a control character).
        /// </summary>
        [EnumMember(Value = "AmbiguousEncoding")]
        AmbiguousEncoding,

        /// <summary>
        /// The path contains a "." or ".." segment.
        /// </summary>
        [EnumMember(Value = "DotSegment")]
        DotSegment
    }
}
