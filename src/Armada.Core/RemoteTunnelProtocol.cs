namespace Armada.Core
{
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Shared tunnel protocol helpers used by Armada instances and the proxy.
    /// </summary>
    public static class RemoteTunnelProtocol
    {
        #region Public-Members

        /// <summary>
        /// Shared serializer settings for tunnel envelopes.
        /// </summary>
        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Wire value of an envelope type (the lowercase name, for example "request").
        /// </summary>
        /// <param name="type">Envelope type.</param>
        /// <returns>Wire value.</returns>
        public static string ToWireType(RemoteTunnelEnvelopeTypeEnum type)
        {
            if (type == RemoteTunnelEnvelopeTypeEnum.Unknown) throw new ArgumentOutOfRangeException(nameof(type), "Unknown has no wire value.");
            return type.ToString().ToLowerInvariant();
        }

        /// <summary>
        /// Parse a wire envelope type. Matching is case-insensitive and exact; anything else is
        /// <see cref="RemoteTunnelEnvelopeTypeEnum.Unknown"/>.
        /// </summary>
        /// <param name="wireType">Value of <see cref="RemoteTunnelEnvelope.Type"/>.</param>
        /// <returns>Envelope type.</returns>
        public static RemoteTunnelEnvelopeTypeEnum ParseEnvelopeType(string? wireType)
        {
            if (String.IsNullOrEmpty(wireType)) return RemoteTunnelEnvelopeTypeEnum.Unknown;
            foreach (RemoteTunnelEnvelopeTypeEnum candidate in Enum.GetValues<RemoteTunnelEnvelopeTypeEnum>())
            {
                if (candidate == RemoteTunnelEnvelopeTypeEnum.Unknown) continue;
                if (String.Equals(wireType, ToWireType(candidate), StringComparison.OrdinalIgnoreCase)) return candidate;
            }

            return RemoteTunnelEnvelopeTypeEnum.Unknown;
        }

        /// <summary>
        /// Build a request envelope.
        /// </summary>
        public static RemoteTunnelEnvelope CreateRequest(string method, object? payload, string? correlationId = null, string? requesterIp = null)
        {
            return new RemoteTunnelEnvelope
            {
                Type = ToWireType(RemoteTunnelEnvelopeTypeEnum.Request),
                CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"),
                Method = method,
                TimestampUtc = DateTime.UtcNow,
                RequesterIp = requesterIp,
                Payload = SerializePayload(payload)
            };
        }

        /// <summary>
        /// Build a response envelope from a request result.
        /// </summary>
        public static RemoteTunnelEnvelope CreateResponse(string? correlationId, RemoteTunnelRequestResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            return new RemoteTunnelEnvelope
            {
                Type = ToWireType(RemoteTunnelEnvelopeTypeEnum.Response),
                CorrelationId = correlationId,
                TimestampUtc = DateTime.UtcNow,
                StatusCode = result.StatusCode,
                Success = result.Success,
                ErrorCode = result.ErrorCode,
                Message = result.Message,
                Payload = SerializePayload(result.Payload)
            };
        }

        /// <summary>
        /// Build an event envelope.
        /// </summary>
        public static RemoteTunnelEnvelope CreateEvent(string method, object? payload)
        {
            return new RemoteTunnelEnvelope
            {
                Type = ToWireType(RemoteTunnelEnvelopeTypeEnum.Event),
                CorrelationId = Guid.NewGuid().ToString("N"),
                Method = method,
                TimestampUtc = DateTime.UtcNow,
                Payload = SerializePayload(payload)
            };
        }

        /// <summary>
        /// Build a ping envelope.
        /// </summary>
        public static RemoteTunnelEnvelope CreatePing(string? correlationId = null)
        {
            return new RemoteTunnelEnvelope
            {
                Type = ToWireType(RemoteTunnelEnvelopeTypeEnum.Ping),
                CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"),
                TimestampUtc = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Build a pong envelope.
        /// </summary>
        public static RemoteTunnelEnvelope CreatePong(string? correlationId = null)
        {
            return new RemoteTunnelEnvelope
            {
                Type = ToWireType(RemoteTunnelEnvelopeTypeEnum.Pong),
                CorrelationId = correlationId,
                TimestampUtc = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Build an error envelope.
        /// </summary>
        public static RemoteTunnelEnvelope CreateError(string? correlationId, string errorCode, string message, int statusCode = 400)
        {
            return new RemoteTunnelEnvelope
            {
                Type = ToWireType(RemoteTunnelEnvelopeTypeEnum.Error),
                CorrelationId = correlationId,
                TimestampUtc = DateTime.UtcNow,
                StatusCode = statusCode,
                Success = false,
                ErrorCode = errorCode,
                Message = message
            };
        }

        /// <summary>
        /// Serialize an arbitrary payload to a JSON element.
        /// </summary>
        public static JsonElement? SerializePayload(object? payload)
        {
            if (payload == null)
            {
                return null;
            }

            return JsonSerializer.SerializeToElement(payload, payload.GetType(), JsonOptions);
        }

        #endregion
    }
}
