namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TUIKit;
    using TUIKit.Content;

    /// <summary>
    /// View JSON: pretty-printed, syntax-highlighted, scrollable, searchable; <see cref="PlainText"/> is what copy and
    /// save use. Not thread-safe.
    /// </summary>
    public class JsonViewer : ScrollTextView
    {
        #region Public-Members

        /// <summary>
        /// Indented JSON text.
        /// </summary>
        public string Json
        {
            get { return _Json; }
            set
            {
                _Json = Indent(value ?? "");
                Invalidate();
            }
        }

        /// <inheritdoc />
        public override string PlainText
        {
            get { return _Json; }
        }

        #endregion

        #region Private-Members

        private string _Json = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="json">JSON text.</param>
        public JsonViewer(string json = "")
        {
            Json = json;
        }

        /// <summary>
        /// Instantiate from an object serialized with the client's JSON settings.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>The viewer.</returns>
        public static JsonViewer FromObject(object? value)
        {
            return new JsonViewer(Armada.Client.ArmadaJson.Serialize(value));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override IReadOnlyList<StyledText> BuildLines()
        {
            if (String.IsNullOrEmpty(_Json)) return new List<StyledText>();
            return SyntaxHighlighter.Highlight(_Json, "json");
        }

        #endregion

        #region Private-Methods

        private static string Indent(string json)
        {
            if (String.IsNullOrWhiteSpace(json)) return "";
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
                }
            }
            catch (JsonException)
            {
                return json;
            }
        }

        #endregion
    }
}
