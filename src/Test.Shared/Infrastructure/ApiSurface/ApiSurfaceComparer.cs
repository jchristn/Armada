namespace Test.Shared.Infrastructure.ApiSurface
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// Compares a live API surface to the frozen baseline (docs/api-surface-1.0.json) under the rules of
    /// docs/COMPATIBILITY.md. Removals and incompatible changes of non-experimental baseline items are breaking; additions
    /// are allowed; compatible changes that deserve a look (type names, defaults, looser authorization) are notes.
    /// </summary>
    public static class ApiSurfaceComparer
    {
        #region Public-Methods

        /// <summary>
        /// Compare two surfaces.
        /// </summary>
        /// <param name="baseline">Frozen baseline.</param>
        /// <param name="live">Live surface.</param>
        /// <returns>Diff.</returns>
        public static ApiSurfaceDiff Compare(ApiSurfaceDocument baseline, ApiSurfaceDocument live)
        {
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));
            if (live == null) throw new ArgumentNullException(nameof(live));
            ApiSurfaceDiff diff = new ApiSurfaceDiff();
            CompareRest(baseline.Rest, live.Rest, diff);
            CompareMcp(baseline.Mcp, live.Mcp, diff);
            CompareWebSocket(baseline.WebSocket, live.WebSocket, diff);
            CompareCli(baseline.Cli, live.Cli, diff);
            CompareSettings(baseline.Settings, live.Settings, diff);
            return diff;
        }

        /// <summary>
        /// Render a diff for a test failure or console output.
        /// </summary>
        /// <param name="diff">Diff.</param>
        /// <param name="maxPerSection">Maximum lines per section.</param>
        /// <returns>Readable text.</returns>
        public static string Format(ApiSurfaceDiff diff, int maxPerSection = 200)
        {
            if (diff == null) throw new ArgumentNullException(nameof(diff));
            StringBuilder sb = new StringBuilder();
            AppendSection(sb, "BREAKING (removed or incompatible; restore it, or deprecate per docs/COMPATIBILITY.md and change it only in a major release)", diff.Breaking, maxPerSection);
            AppendSection(sb, "ADDED (allowed; run scripts/common/generate-api-surface.sh to freeze)", diff.Additions, maxPerSection);
            AppendSection(sb, "NOTES (compatible changes)", diff.Notes, maxPerSection);
            return sb.ToString();
        }

        #endregion

        #region Private-Methods

        private static void AppendSection(StringBuilder sb, string title, List<string> lines, int max)
        {
            sb.AppendLine(title + ": " + lines.Count);
            foreach (string line in lines.Take(max)) sb.AppendLine("  - " + line);
            if (lines.Count > max) sb.AppendLine("  ... " + (lines.Count - max) + " more");
        }

        private static int AuthRank(string level)
        {
            switch (level)
            {
                case "NoAuthRequired": return 0;
                case "Authenticated": return 1;
                case "TenantAdmin": return 2;
                case "AdminOnly": return 3;
                default: return 4;
            }
        }

        private static void CompareAuth(string what, string baseline, string live, ApiSurfaceDiff diff)
        {
            if (String.Equals(baseline, live, StringComparison.Ordinal)) return;
            if (AuthRank(live) > AuthRank(baseline)) diff.Breaking.Add(what + ": authorization tightened " + baseline + " -> " + live);
            else diff.Notes.Add(what + ": authorization loosened " + baseline + " -> " + live);
        }

        private static void CompareRest(List<ApiRestRoute> baseline, List<ApiRestRoute> live, ApiSurfaceDiff diff)
        {
            Dictionary<string, ApiRestRoute> liveByKey = new Dictionary<string, ApiRestRoute>(StringComparer.OrdinalIgnoreCase);
            foreach (ApiRestRoute route in live) liveByKey[route.Method + " " + route.Route] = route;
            HashSet<string> baselineKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ApiRestRoute old in baseline)
            {
                string key = old.Method + " " + old.Route;
                baselineKeys.Add(key);
                if (old.Experimental) continue;
                string what = "REST " + key;
                if (!liveByKey.TryGetValue(key, out ApiRestRoute? current))
                {
                    diff.Breaking.Add(what + ": route removed or renamed");
                    continue;
                }

                if (current.Experimental) diff.Breaking.Add(what + ": a stable route was marked experimental");
                CompareAuth(what, old.Auth, current.Auth, diff);
                if (!old.RequestRequired && current.RequestRequired) diff.Breaking.Add(what + ": request body became required");
                if (!String.Equals(old.RequestType, current.RequestType, StringComparison.Ordinal))
                    diff.Notes.Add(what + ": request type " + (old.RequestType ?? "(none)") + " -> " + (current.RequestType ?? "(none)") + " (check the body stays compatible)");
                foreach (KeyValuePair<string, string> response in old.Responses)
                {
                    if (!current.Responses.TryGetValue(response.Key, out string? type))
                        diff.Notes.Add(what + ": documented response " + response.Key + " (" + response.Value + ") no longer documented");
                    else if (!String.Equals(type, response.Value, StringComparison.Ordinal))
                        diff.Notes.Add(what + ": response " + response.Key + " type " + response.Value + " -> " + type + " (check the body stays compatible)");
                }
            }

            foreach (ApiRestRoute route in live)
            {
                string key = route.Method + " " + route.Route;
                if (!baselineKeys.Contains(key)) diff.Additions.Add("REST " + key + (route.Experimental ? " (experimental)" : ""));
            }
        }

        private static void CompareMcp(List<ApiMcpTool> baseline, List<ApiMcpTool> live, ApiSurfaceDiff diff)
        {
            Dictionary<string, ApiMcpTool> liveByName = live.ToDictionary(t => t.Name, StringComparer.Ordinal);
            HashSet<string> baselineNames = new HashSet<string>(baseline.Select(t => t.Name), StringComparer.Ordinal);

            foreach (ApiMcpTool old in baseline)
            {
                if (old.Experimental) continue;
                string what = "MCP " + old.Name;
                if (!liveByName.TryGetValue(old.Name, out ApiMcpTool? current))
                {
                    diff.Breaking.Add(what + ": tool removed or renamed");
                    continue;
                }

                if (current.Experimental) diff.Breaking.Add(what + ": a stable tool was marked experimental");
                CompareAuth(what, old.Auth, current.Auth, diff);
                Dictionary<string, ApiMcpArgument> liveArgs = current.Arguments.ToDictionary(a => a.Name, StringComparer.Ordinal);
                HashSet<string> oldArgs = new HashSet<string>(old.Arguments.Select(a => a.Name), StringComparer.Ordinal);
                foreach (ApiMcpArgument argument in old.Arguments)
                {
                    if (!liveArgs.TryGetValue(argument.Name, out ApiMcpArgument? now))
                    {
                        diff.Breaking.Add(what + ": argument '" + argument.Name + "' removed or renamed");
                        continue;
                    }

                    if (!String.Equals(argument.Type, now.Type, StringComparison.Ordinal))
                        diff.Breaking.Add(what + ": argument '" + argument.Name + "' type " + argument.Type + " -> " + now.Type);
                    if (!argument.Required && now.Required) diff.Breaking.Add(what + ": argument '" + argument.Name + "' became required");
                    if (argument.Required && !now.Required) diff.Notes.Add(what + ": argument '" + argument.Name + "' became optional");
                }

                foreach (ApiMcpArgument argument in current.Arguments)
                {
                    if (oldArgs.Contains(argument.Name)) continue;
                    if (argument.Required) diff.Breaking.Add(what + ": new required argument '" + argument.Name + "'");
                    else diff.Additions.Add(what + ": optional argument '" + argument.Name + "'");
                }
            }

            foreach (ApiMcpTool tool in live)
                if (!baselineNames.Contains(tool.Name)) diff.Additions.Add("MCP " + tool.Name + (tool.Experimental ? " (experimental)" : ""));
        }

        private static void CompareList(string what, List<string> baseline, List<string> live, ApiSurfaceDiff diff)
        {
            HashSet<string> liveSet = new HashSet<string>(live, StringComparer.Ordinal);
            HashSet<string> baseSet = new HashSet<string>(baseline, StringComparer.Ordinal);
            foreach (string item in baseline) if (!liveSet.Contains(item)) diff.Breaking.Add(what + " '" + item + "' removed or renamed");
            foreach (string item in live) if (!baseSet.Contains(item)) diff.Additions.Add(what + " '" + item + "'");
        }

        private static void CompareWebSocket(ApiWebSocketSurface baseline, ApiWebSocketSurface live, ApiSurfaceDiff diff)
        {
            Dictionary<string, ApiWebSocketEndpoint> liveEndpoints = live.Endpoints.ToDictionary(e => e.Name, StringComparer.Ordinal);
            foreach (ApiWebSocketEndpoint endpoint in baseline.Endpoints)
            {
                if (endpoint.Experimental) continue;
                if (!liveEndpoints.TryGetValue(endpoint.Name, out ApiWebSocketEndpoint? current)) diff.Breaking.Add("WebSocket endpoint '" + endpoint.Name + "' removed");
                else if (!String.Equals(current.Path, endpoint.Path, StringComparison.Ordinal)) diff.Breaking.Add("WebSocket endpoint '" + endpoint.Name + "' path " + endpoint.Path + " -> " + current.Path);
            }

            foreach (ApiWebSocketEndpoint endpoint in live.Endpoints)
                if (!baseline.Endpoints.Any(e => e.Name == endpoint.Name)) diff.Additions.Add("WebSocket endpoint '" + endpoint.Name + "'");

            CompareList("WebSocket route", baseline.Routes, live.Routes, diff);
            CompareList("WebSocket client message field", baseline.ClientMessageFields, live.ClientMessageFields, diff);
            CompareList("WebSocket event envelope field", baseline.EventEnvelopeFields, live.EventEnvelopeFields, diff);
            CompareList("WebSocket command reply field", baseline.CommandReplyFields, live.CommandReplyFields, diff);
            CompareList("WebSocket command", baseline.Commands, live.Commands, diff);

            Dictionary<string, ApiWebSocketEvent> liveEvents = live.Events.ToDictionary(e => e.Type, StringComparer.Ordinal);
            foreach (ApiWebSocketEvent old in baseline.Events)
            {
                string what = "WebSocket event " + old.Type;
                if (!liveEvents.TryGetValue(old.Type, out ApiWebSocketEvent? current))
                {
                    diff.Breaking.Add(what + ": removed or renamed");
                    continue;
                }

                if (!String.Equals(old.Scope, current.Scope, StringComparison.Ordinal)) diff.Breaking.Add(what + ": scope " + old.Scope + " -> " + current.Scope);
                if (old.HasMessage && !current.HasMessage) diff.Breaking.Add(what + ": no longer carries message");
                if (!String.Equals(old.PayloadType, current.PayloadType, StringComparison.Ordinal))
                    diff.Breaking.Add(what + ": payload type " + (old.PayloadType ?? "(fields)") + " -> " + (current.PayloadType ?? "(fields)"));
                HashSet<string> fields = new HashSet<string>(current.Fields, StringComparer.Ordinal);
                foreach (string field in old.Fields) if (!fields.Contains(field)) diff.Breaking.Add(what + ": payload field '" + field + "' removed or renamed");
                foreach (string field in current.Fields) if (!old.Fields.Contains(field)) diff.Additions.Add(what + ": payload field '" + field + "'");
            }

            foreach (ApiWebSocketEvent evt in live.Events)
                if (!baseline.Events.Any(e => e.Type == evt.Type)) diff.Additions.Add("WebSocket event " + evt.Type);
        }

        private static void CompareCli(List<ApiCliCommand> baseline, List<ApiCliCommand> live, ApiSurfaceDiff diff)
        {
            Dictionary<string, ApiCliCommand> liveByName = live.ToDictionary(c => c.Command, StringComparer.Ordinal);
            foreach (ApiCliCommand old in baseline)
            {
                string what = "CLI 'armada " + old.Command + "'";
                if (!liveByName.TryGetValue(old.Command, out ApiCliCommand? current))
                {
                    diff.Breaking.Add(what + ": command removed or renamed");
                    continue;
                }

                Dictionary<string, ApiCliParameter> liveParams = current.Parameters.ToDictionary(p => p.Kind + ":" + p.Name, StringComparer.Ordinal);
                HashSet<string> oldKeys = new HashSet<string>(old.Parameters.Select(p => p.Kind + ":" + p.Name), StringComparer.Ordinal);
                foreach (ApiCliParameter parameter in old.Parameters)
                {
                    string label = parameter.Kind == "option" ? "option --" + parameter.Name : "argument <" + parameter.Name + ">";
                    if (!liveParams.TryGetValue(parameter.Kind + ":" + parameter.Name, out ApiCliParameter? now))
                    {
                        diff.Breaking.Add(what + ": " + label + " removed or renamed");
                        continue;
                    }

                    if (!parameter.Required && now.Required) diff.Breaking.Add(what + ": " + label + " became required");
                    if (!String.IsNullOrEmpty(parameter.Short) && !String.Equals(parameter.Short, now.Short, StringComparison.Ordinal))
                        diff.Breaking.Add(what + ": " + label + " short alias -" + parameter.Short + " removed");
                    if (!String.Equals(parameter.ValueKind, now.ValueKind, StringComparison.Ordinal))
                        diff.Breaking.Add(what + ": " + label + " kind " + parameter.ValueKind + " -> " + now.ValueKind);
                }

                foreach (ApiCliParameter parameter in current.Parameters)
                {
                    if (oldKeys.Contains(parameter.Kind + ":" + parameter.Name)) continue;
                    string label = parameter.Kind == "option" ? "option --" + parameter.Name : "argument <" + parameter.Name + ">";
                    if (parameter.Required) diff.Breaking.Add(what + ": new required " + label);
                    else diff.Additions.Add(what + ": " + label);
                }
            }

            foreach (ApiCliCommand command in live)
                if (!baseline.Any(c => c.Command == command.Command)) diff.Additions.Add("CLI 'armada " + command.Command + "'");
        }

        private static void CompareSettings(List<ApiSettingKey> baseline, List<ApiSettingKey> live, ApiSurfaceDiff diff)
        {
            Dictionary<string, ApiSettingKey> liveByKey = live.ToDictionary(s => s.Key, StringComparer.Ordinal);
            foreach (ApiSettingKey old in baseline)
            {
                if (old.Experimental) continue;
                string what = "Setting " + old.Key;
                if (!liveByKey.TryGetValue(old.Key, out ApiSettingKey? current))
                {
                    diff.Breaking.Add(what + ": key removed or renamed");
                    continue;
                }

                if (current.Experimental) diff.Breaking.Add(what + ": a stable key was marked experimental");
                if (!String.Equals(old.Type, current.Type, StringComparison.Ordinal)) CompareSettingType(what, old, current, diff);
                if (!String.Equals(old.Default, current.Default, StringComparison.Ordinal))
                    diff.Notes.Add(what + ": default " + (old.Default ?? "(object)") + " -> " + (current.Default ?? "(object)"));
            }

            foreach (ApiSettingKey key in live)
                if (!baseline.Any(s => s.Key == key.Key)) diff.Additions.Add("Setting " + key.Key + (key.Experimental ? " (experimental)" : ""));
        }

        private static void CompareSettingType(string what, ApiSettingKey baseline, ApiSettingKey live, ApiSurfaceDiff diff)
        {
            // Enums carry their value list. Adding a value (or changing nullability) is compatible; removing a value or
            // changing the enum is not. Anything else that changes the type label is breaking.
            if (baseline.EnumName != null && live.EnumName != null && baseline.EnumValues != null && live.EnumValues != null
                && String.Equals(baseline.EnumName, live.EnumName, StringComparison.Ordinal))
            {
                HashSet<string> liveValues = new HashSet<string>(live.EnumValues, StringComparer.Ordinal);
                List<string> removed = baseline.EnumValues.Where(v => !liveValues.Contains(v)).ToList();
                if (removed.Count > 0) diff.Breaking.Add(what + ": enum values removed: " + String.Join(", ", removed));
                else diff.Additions.Add(what + ": enum values added (" + live.Type + ")");
                return;
            }

            diff.Breaking.Add(what + ": type " + baseline.Type + " -> " + live.Type);
        }

        #endregion
    }
}
