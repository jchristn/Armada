namespace Armada.Server.WebSocket
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One server-pushed WebSocket event type in the public contract: its type string, who receives it, and the field
    /// names of its <c>data</c> payload (camelCase, as sent on the wire). Events whose payload is a whole serialized
    /// entity name the entity type in <see cref="PayloadType"/> instead of listing fields.
    /// </summary>
    public sealed class WebSocketEventDescriptor
    {
        #region Public-Members

        /// <summary>
        /// Event type string, for example mission.changed.
        /// </summary>
        public string Type { get; set; } = "";

        /// <summary>
        /// Recipients: tenant (sockets of the entity's tenant plus opted-in global admins), user (the owning user's
        /// sockets only), approvers (global admins of the tenant or opted in, the tenant's tenant admins, and the owning
        /// user), or connection (the subscribing socket only).
        /// </summary>
        public string Scope { get; set; } = "tenant";

        /// <summary>
        /// Whether the envelope carries a human-readable <c>message</c>.
        /// </summary>
        public bool HasMessage { get; set; } = false;

        /// <summary>
        /// Entity type serialized as the whole payload, or null when <see cref="Fields"/> lists the payload fields.
        /// </summary>
        public string? PayloadType { get; set; } = null;

        /// <summary>
        /// Field names of the <c>data</c> payload (camelCase).
        /// </summary>
        public List<string> Fields { get; set; } = new List<string>();

        /// <summary>
        /// Whether this is a generic event (fixed <see cref="WebSocketSurface.GenericEventFields"/> payload).
        /// </summary>
        public bool Generic { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public WebSocketEventDescriptor()
        {
        }

        /// <summary>
        /// Instantiate with values.
        /// </summary>
        /// <param name="type">Event type.</param>
        /// <param name="scope">Recipient scope.</param>
        /// <param name="hasMessage">Whether the envelope has a message.</param>
        /// <param name="payloadType">Entity payload type, or null.</param>
        /// <param name="fields">Payload field names.</param>
        /// <param name="generic">Whether the event is generic.</param>
        public WebSocketEventDescriptor(string type, string scope, bool hasMessage, string? payloadType, IEnumerable<string>? fields, bool generic)
        {
            if (String.IsNullOrEmpty(type)) throw new ArgumentNullException(nameof(type));
            Type = type;
            Scope = scope ?? "tenant";
            HasMessage = hasMessage;
            PayloadType = payloadType;
            Fields = fields == null ? new List<string>() : new List<string>(fields);
            Generic = generic;
        }

        #endregion
    }
}
