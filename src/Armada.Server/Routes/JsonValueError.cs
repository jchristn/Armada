namespace Armada.Server.Routes
{
    using System;

    /// <summary>A request-body value that did not bind, described without .NET type names.</summary>
    public sealed class JsonValueError
    {
        /// <summary>Stable error code for clients.</summary>
        public const string InvalidValueCode = "InvalidJsonValue";

        /// <summary>Stable error code for a body that is not well-formed JSON.</summary>
        public const string MalformedCode = "MalformedJson";

        /// <summary>Error code: <see cref="InvalidValueCode"/> or <see cref="MalformedCode"/>.</summary>
        public string Code { get; set; } = InvalidValueCode;

        /// <summary>JSON path of the offending member as sent (for example <c>UsageSelection.Mode</c>), or null for the whole body.</summary>
        public string? Field { get; set; }

        /// <summary>User-facing explanation that says what the value must be.</summary>
        public string Message { get; set; } = String.Empty;
    }
}
