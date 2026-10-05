namespace Armada.Client.Socket
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core;
    using Armada.Core.Enums;

    /// <summary>
    /// A <c>command.error</c> reply on the WebSocket command route: <c>{ type, action, error, code }</c> at the top
    /// level of the message (no <c>data</c>). Branch on <see cref="ErrorCode"/>, not on the English <see cref="Error"/>.
    /// </summary>
    public class CommandErrorMessage
    {
        #region Public-Members

        /// <summary>
        /// Message type, <c>command.error</c>.
        /// </summary>
        public string Type { get; set; } = "";

        /// <summary>
        /// The command action that failed, or null when the server could not read it.
        /// </summary>
        public string? Action { get; set; } = null;

        /// <summary>
        /// Human-readable error text, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Machine-readable reason name (a <see cref="WebSocketCommandErrorCodeEnum"/> member), or null from an older
        /// server.
        /// </summary>
        public string? Code { get; set; } = null;

        /// <summary>
        /// <see cref="Code"/> as a typed reason, or null when absent or not a defined name.
        /// </summary>
        [JsonIgnore]
        public WebSocketCommandErrorCodeEnum? ErrorCode
        {
            get { return EnumNames.ParseOrNull<WebSocketCommandErrorCodeEnum>(Code); }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CommandErrorMessage()
        {
        }

        /// <summary>
        /// Read a <c>command.error</c> reply from a socket message. Returns null for other message types or text that
        /// does not parse.
        /// </summary>
        /// <param name="message">Socket message.</param>
        /// <returns>The reply, or null.</returns>
        public static CommandErrorMessage? From(ArmadaSocketMessage? message)
        {
            if (message == null || !String.Equals(message.Type, ArmadaEventTypes.CommandError, StringComparison.Ordinal)) return null;
            if (String.IsNullOrEmpty(message.RawJson)) return null;
            try
            {
                return JsonSerializer.Deserialize<CommandErrorMessage>(message.RawJson, ArmadaJson.Options);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion
    }
}
