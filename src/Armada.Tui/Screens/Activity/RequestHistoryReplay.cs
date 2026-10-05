namespace Armada.Tui.Screens.Activity
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// A captured request prepared for replay in the API Explorer (the dashboard's <c>buildReplayState</c>): method,
    /// concrete route, route template, and the stored path, query, header, and body values.
    /// </summary>
    public class RequestHistoryReplay
    {
        #region Public-Members

        /// <summary>
        /// HTTP method.
        /// </summary>
        public string Method { get; set; } = "GET";

        /// <summary>
        /// Concrete route.
        /// </summary>
        public string Route { get; set; } = "/";

        /// <summary>
        /// Route template, or null.
        /// </summary>
        public string? RouteTemplate { get; set; } = null;

        /// <summary>
        /// Path parameter values.
        /// </summary>
        public List<KeyValuePair<string, string>> PathValues { get; set; } = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// Query parameter values.
        /// </summary>
        public List<KeyValuePair<string, string>> QueryValues { get; set; } = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// Request header values.
        /// </summary>
        public List<KeyValuePair<string, string>> HeaderValues { get; set; } = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// Request body text.
        /// </summary>
        public string BodyValue { get; set; } = "";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build from a stored record.
        /// </summary>
        /// <param name="record">Record.</param>
        /// <returns>Replay state.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="record"/> is null.</exception>
        public static RequestHistoryReplay From(RequestHistoryRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            RequestHistoryReplay replay = new RequestHistoryReplay();
            replay.Method = record.Entry.Method;
            replay.Route = record.Entry.Route;
            replay.RouteTemplate = record.Entry.RouteTemplate;
            replay.QueryValues = RequestHistoryFormat.ParseDictionary(record.Detail?.QueryParamsJson);
            replay.HeaderValues = RequestHistoryFormat.ParseDictionary(record.Detail?.RequestHeadersJson);
            replay.BodyValue = record.Detail?.RequestBodyText ?? "";
            replay.PathValues = RequestHistoryFormat.ParseDictionary(record.Detail?.PathParamsJson);
            return replay;
        }

        #endregion
    }
}
