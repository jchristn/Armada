namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A raw JSON value from an OpenAPI document (example, default, or enum entry). Whether it is a string is decided
    /// by the JSON token type when it is read (<see cref="ApiExplorerJsonValueConverter"/>), never by inspecting text.
    /// </summary>
    [JsonConverter(typeof(ApiExplorerJsonValueConverter))]
    public class ApiExplorerJsonValue
    {
        #region Public-Members

        /// <summary>
        /// True when the value is a JSON string.
        /// </summary>
        public bool IsString { get; }

        /// <summary>
        /// The string value when <see cref="IsString"/>, else null.
        /// </summary>
        public string? StringValue { get; }

        /// <summary>
        /// The value as compact JSON text (for a string, the quoted JSON string).
        /// </summary>
        public string Json { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="isString">Whether the value is a JSON string.</param>
        /// <param name="stringValue">String value when a string.</param>
        /// <param name="json">Compact JSON text.</param>
        public ApiExplorerJsonValue(bool isString, string? stringValue, string json)
        {
            IsString = isString;
            StringValue = isString ? (stringValue ?? "") : null;
            Json = json ?? throw new ArgumentNullException(nameof(json));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The JavaScript <c>String(x)</c> of the value: strings unquoted, other values as JSON text.
        /// </summary>
        /// <returns>Text.</returns>
        public override string ToString()
        {
            return IsString ? StringValue! : Json;
        }

        #endregion
    }
}
