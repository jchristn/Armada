namespace Armada.Server.Routes
{
    using System;
    using System.Text.Json;
    using Armada.Core.Models;
    using WatsonWebserver.Core;

    /// <summary>
    /// Reads the paging and filter query of a <c>POST .../enumerate</c> route: the JSON body (optional) with querystring
    /// values applied on top, the same way the existing enumerate routes read it.
    /// </summary>
    internal static class EnumerationRequestReader
    {
        #region Public-Methods

        /// <summary>
        /// Read the query.
        /// </summary>
        /// <param name="req">Request.</param>
        /// <param name="options">Serializer options for the body.</param>
        /// <returns>The query (page 1 of 10 when nothing was sent).</returns>
        /// <exception cref="ArgumentNullException">Thrown when req or options is null.</exception>
        public static EnumerationQuery Read(ApiRequest req, JsonSerializerOptions options)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (options == null) throw new ArgumentNullException(nameof(options));
            string body = req.Http.Request.DataAsString;
            EnumerationQuery query = String.IsNullOrWhiteSpace(body)
                ? new EnumerationQuery()
                : JsonSerializer.Deserialize<EnumerationQuery>(body, options) ?? new EnumerationQuery();
            query.ApplyQuerystringOverrides(key => QueryValueReader.Read(req.Query, key));
            return query;
        }

        #endregion
    }
}
