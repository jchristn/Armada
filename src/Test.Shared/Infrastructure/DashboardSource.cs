namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Parses the web dashboard's source (routes, hub tabs, API client exports, WebSocket events, Server settings
    /// fields) for the parity tests. Mirrors scripts/tui/generate-parity-manifest.py.
    /// </summary>
    public static class DashboardSource
    {
        /// <summary>
        /// Exports of client.ts that do not call the server.
        /// </summary>
        public static readonly HashSet<string> NonServerExports = new HashSet<string>(StringComparer.Ordinal)
        {
            "ApiError", "apiErrorCode", "apiErrorMessage", "isApiStatus", "setAuthToken", "setOnUnauthorized", "camelizeKeys", "encodeBrowsePath",
            "configureClient", "getClientBaseUrl", "BULK_DELETE_CHUNK", "CLEANUP_KEY_CHUNK", "MAX_WORKSPACE_PAGES", "deleteInChunks"
        };

        /// <summary>
        /// Hub pages and the TUI route and query parameter they map to.
        /// </summary>
        public static readonly Dictionary<string, string> HubPages = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CaptainsHub"] = "/captains?tab=", ["DeliveryHub"] = "/delivery?tab=", ["DispatchHub"] = "/dispatch?tab=",
            ["MissionsHub"] = "/missions?tab=", ["ServerHub"] = "/server?tab=", ["VesselsHub"] = "/vessels?tab=",
            ["Configuration"] = "/configuration?tab=", ["Activity"] = "/activity?source=", ["FleetActions"] = "/fleet-actions?tab=",
            ["CliPermissions"] = "/cli-permissions?tab="
        };

        /// <summary>
        /// Repository root.
        /// </summary>
        public static string RepoRoot()
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir, "src", "Armada.Dashboard", "src", "App.tsx"))) return dir;
                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("Could not find the repository root (src/Armada.Dashboard/src/App.tsx) above " + AppContext.BaseDirectory);
        }

        /// <summary>
        /// Dashboard src directory.
        /// </summary>
        public static string Src()
        {
            return Path.Combine(RepoRoot(), "src", "Armada.Dashboard", "src");
        }

        /// <summary>
        /// Route paths in App.tsx (index route as "/").
        /// </summary>
        public static List<string> Routes()
        {
            string src = File.ReadAllText(Path.Combine(Src(), "App.tsx"));
            List<string> routes = new List<string>();
            foreach (Match m in Regex.Matches(src, "<Route\\s+(index\\s+)?(?:path=\"([^\"]+)\")?"))
            {
                if (m.Groups[1].Success) routes.Add("/");
                else if (m.Groups[2].Success) routes.Add("/" + m.Groups[2].Value);
            }

            return routes;
        }

        /// <summary>
        /// Hub tabs as "Page:key".
        /// </summary>
        public static List<string> Tabs()
        {
            List<string> tabs = new List<string>();
            foreach (string page in HubPages.Keys)
            {
                string path = Path.Combine(Src(), "pages", page + ".tsx");
                foreach (Match m in Regex.Matches(File.ReadAllText(path), "\\{ key: '([^']+)', label: '([^']+)'"))
                    tabs.Add(page + ":" + m.Groups[1].Value);
            }

            return tabs;
        }

        /// <summary>
        /// Exported server-calling functions of api/client.ts.
        /// </summary>
        public static List<string> ApiExports()
        {
            string src = File.ReadAllText(Path.Combine(Src(), "api", "client.ts"));
            return Regex.Matches(src, "^export (?:const|async function|function) (\\w+)", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value)
                .Where(n => !NonServerExports.Contains(n))
                .ToList();
        }

        /// <summary>
        /// The requests each server-calling api/client.ts export sends, parsed from the URL literals passed to
        /// get/post/put/del/patch, enumerateFirstPage and enumeratePage (POSTs), request, proxyRequest, and fetch. String constants and one-line path helpers are expanded; an
        /// interpolated path segment becomes <c>x</c> and an interpolation that is not a whole segment (a query builder)
        /// is dropped. An export that only calls other exports yields no request.
        /// </summary>
        /// <returns>Requests, in source order.</returns>
        public static List<DashboardApiCall> ApiCalls()
        {
            string src = File.ReadAllText(Path.Combine(Src(), "api", "client.ts"));
            Dictionary<string, string> constants = Regex.Matches(src, "^const (\\w+) = '([^']*)';", RegexOptions.Multiline)
                .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);
            Dictionary<string, string> helpers = Regex.Matches(src, "^const (\\w+) = \\([^)]*\\) => `([^`]*)`;", RegexOptions.Multiline)
                .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);

            List<Match> exports = Regex.Matches(src, "^export (?:const|async function|function) (\\w+)", RegexOptions.Multiline).ToList();
            Regex verbCall = new Regex("\\b(get|post|put|del|patch|enumerateFirstPage|enumeratePage)\\s*(?:<[^()]*?>)?\\s*\\(\\s*(?:([`'\"])((?:\\\\.|(?!\\2).)*)\\2|(\\w+)\\()", RegexOptions.Singleline);
            Regex proxyCall = new Regex("(?:proxyRequest|\\brequest)(?:<[^()]*?>)?\\(\\s*'(\\w+)'\\s*,\\s*'([^']*)'");
            Regex fetchCall = new Regex("fetch\\w*\\(\\s*`\\$\\{BASE_URL\\}([^`]*)`\\s*,\\s*\\{\\s*method:\\s*'(\\w+)'");

            List<DashboardApiCall> calls = new List<DashboardApiCall>();
            for (int i = 0; i < exports.Count; i++)
            {
                string name = exports[i].Groups[1].Value;
                if (NonServerExports.Contains(name)) continue;
                int end = i + 1 < exports.Count ? exports[i + 1].Index : src.Length;
                string body = src.Substring(exports[i].Index, end - exports[i].Index);

                foreach (Match m in verbCall.Matches(body))
                {
                    string url = m.Groups[3].Success ? m.Groups[3].Value
                        : helpers.TryGetValue(m.Groups[4].Value, out string? helper) ? helper : "";
                    // Only a path is a request: res.headers.get('Content-Type') and similar are not.
                    if (url.Length == 0 || !(url.StartsWith("/", StringComparison.Ordinal) || url.StartsWith("${", StringComparison.Ordinal))) continue;
                    string verb = m.Groups[1].Value == "del" ? "DELETE" : (m.Groups[1].Value == "enumerateFirstPage" || m.Groups[1].Value == "enumeratePage") ? "POST" : m.Groups[1].Value.ToUpperInvariant();
                    string path = NormalizePath(url, constants, helpers);
                    bool hasQuery = url.Contains('?') || System.Text.RegularExpressions.Regex.IsMatch(url, "[^/]\\$\\{[^}]*(?:Query|query)");
                    calls.Add(new DashboardApiCall { Export = name, Method = verb, Path = path, HasQuery = hasQuery });
                }

                foreach (Match m in proxyCall.Matches(body))
                    calls.Add(new DashboardApiCall { Export = name, Method = m.Groups[1].Value.ToUpperInvariant(), Path = NormalizePath(m.Groups[2].Value, constants, helpers) });

                foreach (Match m in fetchCall.Matches(body))
                    calls.Add(new DashboardApiCall { Export = name, Method = m.Groups[2].Value.ToUpperInvariant(), Path = NormalizePath(m.Groups[1].Value, constants, helpers) });
            }

            return calls;
        }

        /// <summary>
        /// WebSocket event types the dashboard handles.
        /// </summary>
        public static List<string> Events()
        {
            Regex re = new Regex("'((?:mission|voyage|captain|captain-selection|deployment|objective|incident|planning-session|objective-refinement-session|ask)\\.[a-z][a-z.\\-]*)'");
            HashSet<string> found = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in Directory.EnumerateFiles(Src(), "*.*", SearchOption.AllDirectories))
            {
                if (!(file.EndsWith(".ts") || file.EndsWith(".tsx")) || file.Contains(".test.")) continue;
                foreach (Match m in re.Matches(File.ReadAllText(file)))
                {
                    string v = m.Groups[1].Value;
                    if (v.EndsWith(".") || v.StartsWith("mission.rules")) continue;
                    found.Add(v);
                }
            }

            return found.OrderBy(s => s, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Server-page settings fields.
        /// </summary>
        public static List<string> SettingsFields()
        {
            HashSet<string> fields = new HashSet<string>(StringComparer.Ordinal);
            string server = File.ReadAllText(Path.Combine(Src(), "pages", "Server.tsx"));
            foreach (Match m in Regex.Matches(server, "settings\\??\\.((?:remoteControl\\??\\.)?[a-zA-Z]+)"))
            {
                string name = m.Groups[1].Value.Replace("?.", ".");
                if (name == "json" || name == "import" || name == "fleetActions" || name == "remoteControl") continue;
                fields.Add("settings." + name);
            }

            string imp = File.ReadAllText(Path.Combine(Src(), "components", "settings", "ImportFleetActionSettings.tsx"));
            foreach (Match m in Regex.Matches(imp, "importDraft\\??\\.([a-zA-Z]+)")) fields.Add("settings.import." + m.Groups[1].Value);
            foreach (Match m in Regex.Matches(imp, "fleetDraft\\??\\.([a-zA-Z]+)")) fields.Add("settings.fleetActions." + m.Groups[1].Value);
            string rh = File.ReadAllText(Path.Combine(Src(), "components", "vessels", "health", "RepositoryHealthSettingsSection.tsx"));
            foreach (Match m in Regex.Matches(rh, "settings\\??\\.([a-zA-Z]+)")) fields.Add("settings.repositoryHealth." + m.Groups[1].Value);
            // The field tables (REPOSITORY_HEALTH_FIELDS, ..._THRESHOLD_FIELDS) live in lib/settingsRanges.ts, shared with the mobile app.
            string ranges = File.ReadAllText(Path.Combine(Src(), "lib", "settingsRanges.ts"));
            MatchCollection rhKeys = Regex.Matches(rh + ranges, "\\{ key: '([a-zA-Z]+)', label: msg\\(");
            if (rhKeys.Count == 0) throw new InvalidOperationException("Repository health field tables not found in the dashboard source; update SettingsFields().");
            foreach (Match m in rhKeys)
            {
                string key = m.Groups[1].Value;
                bool threshold = key.EndsWith("Warn", StringComparison.Ordinal) || key.EndsWith("Fail", StringComparison.Ordinal);
                fields.Add((threshold ? "settings.repositoryHealth.thresholds." : "settings.repositoryHealth.") + key);
            }

            string perm = File.ReadAllText(Path.Combine(Src(), "components", "settings", "CliPermissionSettings.tsx"));
            Match draft = Regex.Match(perm, "interface Draft \\{([^}]*)\\}");
            if (!draft.Success) throw new InvalidOperationException("CliPermissionSettings Draft interface not found in the dashboard source; update SettingsFields().");
            if (draft.Success)
            {
                foreach (Match f in Regex.Matches(draft.Groups[1].Value, "^\\s*([a-zA-Z]+):", RegexOptions.Multiline)) fields.Add("settings.permissions." + f.Groups[1].Value);
            }

            // RETENTION_FIELDS lives in lib/settingsRanges.ts, shared with the mobile app.
            Match retention = Regex.Match(ranges, "const RETENTION_FIELDS: RetentionField\\[\\] = \\[([^\\]]*)\\]");
            if (!retention.Success) throw new InvalidOperationException("RETENTION_FIELDS not found in the dashboard source; update SettingsFields().");
            if (retention.Success)
            {
                foreach (Match f in Regex.Matches(retention.Groups[1].Value, "'([a-zA-Z]+)'")) fields.Add("settings.retention." + f.Groups[1].Value);
            }

            // ASK_FIELDS (the edited Ask Armada settings) lives in lib/settingsRanges.ts too; mirrors dashboard_surfaces.py.
            Match ask = Regex.Match(ranges, "const ASK_FIELDS: AskSettingsField\\[\\] = \\[([^\\]]*)\\]");
            if (!ask.Success) throw new InvalidOperationException("ASK_FIELDS not found in the dashboard source; update SettingsFields().");
            foreach (Match f in Regex.Matches(ask.Groups[1].Value, "'([a-zA-Z]+)'")) fields.Add("settings.ask." + f.Groups[1].Value);

            return fields.OrderBy(s => s, StringComparer.Ordinal).ToList();
        }

        private static string NormalizePath(string url, Dictionary<string, string> constants, Dictionary<string, string> helpers)
        {
            // Expand ${...} interpolations with brace matching: a path helper call or a string constant expands in place,
            // a whole path segment becomes the parameter x, and anything else (a query builder) is dropped.
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int pos = 0;
            while (pos < url.Length)
            {
                if (url[pos] == '$' && pos + 1 < url.Length && url[pos + 1] == '{')
                {
                    int depth = 0;
                    int close = pos + 1;
                    for (; close < url.Length; close++)
                    {
                        if (url[close] == '{') depth++;
                        else if (url[close] == '}' && --depth == 0) break;
                    }

                    string expr = url.Substring(pos + 2, Math.Max(0, close - pos - 2)).Trim();
                    Match helperCall = Regex.Match(expr, "^(\\w+)\\(");
                    bool wholeSegment = sb.Length > 0 && sb[sb.Length - 1] == '/' && (close + 1 >= url.Length || url[close + 1] == '/' || url[close + 1] == '?');
                    if (helperCall.Success && helpers.TryGetValue(helperCall.Groups[1].Value, out string? helper)) sb.Append(NormalizePath(helper, constants, helpers));
                    else if (constants.TryGetValue(expr, out string? constant)) sb.Append(constant);
                    else if (wholeSegment) sb.Append('x');
                    pos = close + 1;
                    continue;
                }

                if (url[pos] == '?') break;
                sb.Append(url[pos]);
                pos++;
            }

            return sb.ToString();
        }
    }
}
