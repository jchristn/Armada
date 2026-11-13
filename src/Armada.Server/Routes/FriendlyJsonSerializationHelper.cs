namespace Armada.Server.Routes
{
    using System;
    using System.Text.Json;
    using WatsonWebserver.Core;

    /// <summary>
    /// Wraps the Admiral's JSON serializer so a request body that does not bind (on every typed route, before any handler
    /// runs) fails with a message naming the field and what it accepts, never a .NET type name.
    /// </summary>
    public sealed class FriendlyJsonSerializationHelper : ISerializationHelper
    {
        #region Private-Members

        private readonly ISerializationHelper _Inner;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Wrap a serializer.</summary>
        /// <param name="inner">The serializer that does the work.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is null.</exception>
        public FriendlyJsonSerializationHelper(ISerializationHelper inner)
        {
            _Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        #endregion

        #region Public-Methods

        /// <summary>Deserialize, describing a binding failure in user terms.</summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="json">JSON text.</param>
        /// <returns>Instance.</returns>
        /// <exception cref="WebserverException">Thrown as a 400 with <see cref="JsonValueError"/> data when the body does not bind.</exception>
        public T DeserializeJson<T>(string json)
        {
            try
            {
                return _Inner.DeserializeJson<T>(json);
            }
            catch (JsonException error)
            {
                JsonValueError described = JsonValueErrorTranslator.Describe(error);
                WebserverException badRequest = new WebserverException(ApiResultEnum.BadRequest, described.Message, error);
                badRequest.Data = described;
                throw badRequest;
            }
        }

        /// <summary>Serialize.</summary>
        /// <param name="obj">Object.</param>
        /// <param name="pretty">Pretty print.</param>
        /// <returns>JSON text.</returns>
        public string SerializeJson(object obj, bool pretty = true) => _Inner.SerializeJson(obj, pretty);

        #endregion
    }
}
