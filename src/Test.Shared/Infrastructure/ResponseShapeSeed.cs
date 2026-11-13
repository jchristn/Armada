namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Test.Shared.Infrastructure.ApiSurface;

    /// <summary>
    /// The entities E2E.ResponseShape seeds, and how a route template or MCP tool's arguments are filled from them.
    /// </summary>
    public sealed class ResponseShapeSeed
    {
        #region Public-Members

        /// <summary>
        /// Seeded fleet.
        /// </summary>
        public string FleetId { get; set; } = "";

        /// <summary>
        /// Seeded vessel.
        /// </summary>
        public string VesselId { get; set; } = "";

        /// <summary>
        /// Seeded captain.
        /// </summary>
        public string CaptainId { get; set; } = "";

        /// <summary>
        /// Seeded voyage.
        /// </summary>
        public string VoyageId { get; set; } = "";

        /// <summary>
        /// Seeded mission.
        /// </summary>
        public string MissionId { get; set; } = "";

        #endregion

        #region Public-Methods

        /// <summary>
        /// The request path for a route template with every path parameter filled from the seed, or null when a
        /// parameter has no seeded value.
        /// </summary>
        /// <param name="template">Route template, for example /api/v1/fleets/{id}.</param>
        /// <returns>Path, or null.</returns>
        public string? Resolve(string template)
        {
            if (String.IsNullOrEmpty(template)) return null;
            string[] segments = template.Split('/');
            string resource = "";
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (!segment.StartsWith("{", StringComparison.Ordinal))
                {
                    resource = segment;
                    continue;
                }

                string name = segment.Trim('{', '}');
                string? value = name == "id" ? ValueForResource(resource) : ValueForName(name);
                if (String.IsNullOrEmpty(value)) return null;
                segments[i] = value;
            }

            return String.Join("/", segments);
        }

        /// <summary>
        /// Arguments for a read-only MCP tool: each id argument the seed can fill, plus pageSize 1000 when the tool pages.
        /// </summary>
        /// <param name="tool">Tool.</param>
        /// <returns>Arguments.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="tool"/> is null.</exception>
        public Dictionary<string, object> ArgumentsFor(ApiMcpTool tool)
        {
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (ApiMcpArgument argument in tool.Arguments)
            {
                string? value = ValueForName(argument.Name);
                if (value != null) result[argument.Name] = value;
                else if (argument.Name == "pageSize") result[argument.Name] = 1000;
                else if (argument.Name.StartsWith("include", StringComparison.Ordinal) && argument.Type == "boolean") result[argument.Name] = true;
            }

            if (tool.Name == "voyage_status") result["summary"] = false;
            return result;
        }

        #endregion

        #region Private-Methods

        private string? ValueForResource(string resource)
        {
            switch (resource)
            {
                case "fleets": return FleetId;
                case "vessels": return VesselId;
                case "captains": return CaptainId;
                case "voyages": return VoyageId;
                case "missions": return MissionId;
                default: return null;
            }
        }

        private string? ValueForName(string name)
        {
            switch (name)
            {
                case "fleetId": return FleetId;
                case "vesselId": return VesselId;
                case "captainId": return CaptainId;
                case "voyageId": return VoyageId;
                case "missionId": return MissionId;
                default: return null;
            }
        }

        #endregion
    }
}
