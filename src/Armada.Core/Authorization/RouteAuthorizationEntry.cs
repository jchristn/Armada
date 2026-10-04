namespace Armada.Core.Authorization
{
    using System;

    /// <summary>
    /// One declared REST route in <see cref="RouteAuthorizationRegistry"/>: method, template, its path segments, and
    /// the requirement.
    /// </summary>
    public sealed class RouteAuthorizationEntry
    {
        #region Public-Members

        /// <summary>
        /// Upper-case HTTP method.
        /// </summary>
        public string Method { get; }

        /// <summary>
        /// Route template, for example /api/v1/fleets/{id}.
        /// </summary>
        public string Template { get; }

        /// <summary>
        /// Template path segments.
        /// </summary>
        public string[] Segments { get; }

        /// <summary>
        /// Declared requirement.
        /// </summary>
        public AuthorizationRequirement Requirement { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="template">Route template.</param>
        /// <param name="segments">Template path segments.</param>
        /// <param name="requirement">Declared requirement.</param>
        public RouteAuthorizationEntry(string method, string template, string[] segments, AuthorizationRequirement requirement)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Template = template ?? throw new ArgumentNullException(nameof(template));
            Segments = segments ?? throw new ArgumentNullException(nameof(segments));
            Requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
        }

        #endregion
    }
}
