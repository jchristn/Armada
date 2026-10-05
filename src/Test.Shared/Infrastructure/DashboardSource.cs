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
            "ApiError", "apiErrorCode", "isApiStatus", "setAuthToken", "setOnUnauthorized", "camelizeKeys", "encodeBrowsePath"
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
        /// WebSocket event types the dashboard handles.
        /// </summary>
        public static List<string> Events()
        {
            Regex re = new Regex("'((?:mission|voyage|captain|deployment|objective|incident|planning-session|objective-refinement-session|ask)\\.[a-z][a-z.\\-]*)'");
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
            foreach (Match m in Regex.Matches(rh, "\\{ key: '([a-zA-Z]+)', label: msg\\("))
            {
                string key = m.Groups[1].Value;
                bool threshold = key.EndsWith("Warn", StringComparison.Ordinal) || key.EndsWith("Fail", StringComparison.Ordinal);
                fields.Add((threshold ? "settings.repositoryHealth.thresholds." : "settings.repositoryHealth.") + key);
            }

            string perm = File.ReadAllText(Path.Combine(Src(), "components", "settings", "CliPermissionSettings.tsx"));
            Match draft = Regex.Match(perm, "interface Draft \\{([^}]*)\\}");
            if (draft.Success)
            {
                foreach (Match f in Regex.Matches(draft.Groups[1].Value, "^\\s*([a-zA-Z]+):", RegexOptions.Multiline)) fields.Add("settings.permissions." + f.Groups[1].Value);
            }

            string ret = File.ReadAllText(Path.Combine(Src(), "components", "settings", "RetentionSettings.tsx"));
            Match retention = Regex.Match(ret, "const FIELDS: RetentionField\\[\\] = \\[([^\\]]*)\\]");
            if (retention.Success)
            {
                foreach (Match f in Regex.Matches(retention.Groups[1].Value, "'([a-zA-Z]+)'")) fields.Add("settings.retention." + f.Groups[1].Value);
            }

            return fields.OrderBy(s => s, StringComparer.Ordinal).ToList();
        }
    }
}
