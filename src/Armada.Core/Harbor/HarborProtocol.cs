namespace Armada.Core.Harbor
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Shared framing for the Harbor link. Both the Admiral and the Harbor serialize and deserialize
    /// <see cref="HarborMessage"/> instances through this type so the wire format stays identical on both
    /// ends. Messages are polymorphic on a "type" discriminator.
    /// </summary>
    public static class HarborProtocol
    {
        #region Public-Members

        /// <summary>
        /// The protocol version this build speaks. Bump on any breaking change to the message set.
        /// </summary>
        public static readonly string Version = "1.0";

        /// <summary>
        /// Capability a Harbor advertises when it creates mission docks on its own host (the dock, file, and Harbor-side
        /// repository messages). An Admiral routes a mission's dock to a Harbor only when it advertises this; a Harbor
        /// without it gets the earlier behavior (docks on the Admiral's host).
        /// </summary>
        public static readonly string DockCapability = "harbor-docks";

        /// <summary>
        /// Capability a Harbor advertises when it serves operations in a vessel's checkout on its host for the Admiral
        /// (check runs, Workspace, readiness, and health): file requests with a root, confined to the checkout the Harbor
        /// maps the vessel to or to its docks folder. An Admiral sends checkout operations only to a Harbor that
        /// advertises this.
        /// </summary>
        public static readonly string CheckoutCapability = "harbor-checkouts";

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Serialize a message to its JSON wire form.
        /// </summary>
        /// <param name="message">Message to serialize.</param>
        /// <returns>JSON text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is null.</exception>
        public static string Serialize(HarborMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            return JsonSerializer.Serialize<HarborMessage>(message, _Options);
        }

        /// <summary>
        /// Deserialize a message from its JSON wire form.
        /// </summary>
        /// <param name="json">JSON text.</param>
        /// <returns>The typed message.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="json"/> is null or empty.</exception>
        /// <exception cref="FormatException">Thrown when the payload is not a valid Harbor message.</exception>
        public static HarborMessage Deserialize(string json)
        {
            if (String.IsNullOrWhiteSpace(json)) throw new ArgumentNullException(nameof(json));
            try
            {
                HarborMessage? message = JsonSerializer.Deserialize<HarborMessage>(json, _Options);
                if (message == null) throw new FormatException("Harbor message deserialized to null.");
                return message;
            }
            catch (JsonException e)
            {
                throw new FormatException("Invalid Harbor message payload: " + e.Message, e);
            }
        }

        #endregion
    }
}
