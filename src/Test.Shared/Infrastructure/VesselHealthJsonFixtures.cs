namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// Captured tool output used as fixtures by the vessel health dependency suite. Shapes match dotnet SDK 10
    /// (dotnet list package --format json, schema version 1) and npm 10 (npm outdated --json, npm audit --json v2).
    /// </summary>
    public static class VesselHealthJsonFixtures
    {
        /// <summary>
        /// dotnet list package --outdated --format json for two projects: one major, one minor, one patch drift.
        /// </summary>
        public const string DotnetOutdated = @"{
  ""version"": 1,
  ""parameters"": ""--outdated"",
  ""sources"": [ ""https://api.nuget.org/v3/index.json"" ],
  ""projects"": [
    {
      ""path"": ""/repo/src/App/App.csproj"",
      ""frameworks"": [
        {
          ""framework"": ""net8.0"",
          ""topLevelPackages"": [
            { ""id"": ""Newtonsoft.Json"", ""requestedVersion"": ""12.0.1"", ""resolvedVersion"": ""12.0.1"", ""latestVersion"": ""13.0.4"" },
            { ""id"": ""Serilog"", ""requestedVersion"": ""3.0.0"", ""resolvedVersion"": ""3.0.0"", ""latestVersion"": ""3.1.1"" }
          ]
        },
        {
          ""framework"": ""net10.0"",
          ""topLevelPackages"": [
            { ""id"": ""Newtonsoft.Json"", ""requestedVersion"": ""12.0.1"", ""resolvedVersion"": ""12.0.1"", ""latestVersion"": ""13.0.4"" }
          ]
        }
      ]
    },
    {
      ""path"": ""/repo/src/Lib/Lib.csproj"",
      ""frameworks"": [
        {
          ""framework"": ""net8.0"",
          ""topLevelPackages"": [
            { ""id"": ""Polly"", ""requestedVersion"": ""8.4.0"", ""resolvedVersion"": ""8.4.0"", ""latestVersion"": ""8.4.2-beta.1+build"" }
          ]
        }
      ]
    }
  ]
}";

        /// <summary>
        /// dotnet list package --outdated --format json with nothing outdated.
        /// </summary>
        public const string DotnetOutdatedNone = @"{ ""version"": 1, ""parameters"": ""--outdated"", ""projects"": [ { ""path"": ""/repo/App.csproj"", ""frameworks"": [] } ] }";

        /// <summary>
        /// dotnet list package --vulnerable --format json with one High vulnerability.
        /// </summary>
        public const string DotnetVulnerable = @"{
  ""version"": 1,
  ""parameters"": ""--vulnerable"",
  ""projects"": [
    {
      ""path"": ""/repo/src/App/App.csproj"",
      ""frameworks"": [
        {
          ""framework"": ""net8.0"",
          ""topLevelPackages"": [
            { ""id"": ""Newtonsoft.Json"", ""requestedVersion"": ""12.0.1"", ""resolvedVersion"": ""12.0.1"",
              ""vulnerabilities"": [ { ""severity"": ""High"", ""advisoryurl"": ""https://github.com/advisories/GHSA-5crp-9r3c-p9vr"" } ] }
          ]
        }
      ]
    }
  ]
}";

        /// <summary>
        /// dotnet list package output when restore fails (exit code 1).
        /// </summary>
        public const string DotnetRestoreFailed = @"{
   ""version"": 1,
   ""problems"": [ { ""text"": ""Restore failed. Run `dotnet restore` for more details on the issue."", ""level"": ""error"" } ]
}";

        /// <summary>
        /// npm outdated --json output (npm exits 1 when anything is outdated).
        /// </summary>
        public const string NpmOutdated = @"{
  ""lodash"": { ""current"": ""4.17.15"", ""wanted"": ""4.17.21"", ""latest"": ""4.17.21"", ""dependent"": ""app"", ""location"": ""node_modules/lodash"" },
  ""react"": { ""current"": ""17.0.2"", ""wanted"": ""17.0.2"", ""latest"": ""18.3.1"", ""dependent"": ""app"", ""location"": ""node_modules/react"" },
  ""left-pad"": { ""wanted"": ""1.3.0"", ""latest"": ""1.3.0"", ""dependent"": ""app"", ""location"": """" }
}";

        /// <summary>
        /// npm audit --json (report version 2) with a critical and a moderate vulnerability.
        /// </summary>
        public const string NpmAudit = @"{
  ""auditReportVersion"": 2,
  ""vulnerabilities"": {
    ""minimist"": { ""name"": ""minimist"", ""severity"": ""critical"", ""isDirect"": false, ""via"": [ ""mkdirp"", { ""source"": 1, ""url"": ""https://github.com/advisories/GHSA-xvch-5gv4-984h"" } ], ""range"": ""<0.2.4"", ""fixAvailable"": true },
    ""nth-check"": { ""name"": ""nth-check"", ""severity"": ""moderate"", ""isDirect"": false, ""via"": [], ""range"": ""<2.0.1"", ""fixAvailable"": false }
  },
  ""metadata"": { ""vulnerabilities"": { ""info"": 0, ""low"": 0, ""moderate"": 1, ""high"": 0, ""critical"": 1, ""total"": 2 } }
}";

        /// <summary>
        /// npm audit --json with no vulnerabilities.
        /// </summary>
        public const string NpmAuditClean = @"{ ""auditReportVersion"": 2, ""vulnerabilities"": {}, ""metadata"": { ""vulnerabilities"": { ""total"": 0 } } }";

        /// <summary>
        /// npm error envelope when the lockfile is missing.
        /// </summary>
        public const string NpmErrorNoLock = @"{ ""error"": { ""code"": ""ENOLOCK"", ""summary"": ""This command requires an existing lockfile."", ""detail"": ""Try creating one first with: npm i --package-lock-only"" } }";
    }
}
