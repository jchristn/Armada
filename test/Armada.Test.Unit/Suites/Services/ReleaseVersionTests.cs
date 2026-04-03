namespace Armada.Test.Unit.Suites.Services
{
    using System.IO;
    using System.Text.RegularExpressions;
    using Armada.Core;
    using Armada.Test.Common;

    public class ReleaseVersionTests : TestSuite
    {
        public override string Name => "Release Version";

        protected override async Task RunTestsAsync()
        {
            await RunTest("ProductVersion And Shared Build Props Match V050", () =>
            {
                string propsContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Directory.Build.props"));
                MatchCollection versionMatches = Regex.Matches(propsContents, @"<Version>\s*([^<]+)\s*</Version>");

                AssertTrue(versionMatches.Count == 1, "Directory.Build.props should contain exactly one Version element");
                Match versionMatch = versionMatches[0];
                AssertEqual("0.5.0", Constants.ProductVersion);
                AssertEqual(Constants.ProductVersion, versionMatch.Groups[1].Value.Trim());
            });

            await RunTest("Helm Program Uses ProductVersion Constant", () =>
            {
                string programContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Armada.Helm", "Program.cs"));

                AssertContains("\"v\" + Constants.ProductVersion", programContents, "Helm banner/help version should come from Constants.ProductVersion");
                AssertContains("AnsiConsole.MarkupLine(\"[dim]Multi-Agent Orchestration System  \" + _VersionLabel + \"[/]\");", programContents, "Helm subtitle should render the shared version label");
                AssertContains("config.SetApplicationVersion(Constants.ProductVersion);", programContents, "Helm CLI version should come from Constants.ProductVersion");
                AssertFalse(programContents.Contains("0.3.0"), "Helm entry point should not contain the stale 0.3.0 literal");
                AssertFalse(programContents.Contains("\"0.5.0\""), "Helm entry point should not contain a hard-coded release version literal");
                AssertFalse(programContents.Contains("\"v0.5.0\""), "Helm entry point should compose the prefixed version instead of hard-coding it");
                AssertFalse(programContents.Contains("SetApplicationVersion(\"0.5.0\")"), "Helm CLI version should not be hard-coded");
            });

            await RunTest("Source MCP Helpers Use Net10 Framework", () =>
            {
                string mcpConfigHelperContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Armada.Helm", "Commands", "McpConfigHelper.cs"));
                string installMcpBatContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "install-mcp.bat"));
                string installMcpShContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "install-mcp.sh"));
                string removeMcpBatContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "remove-mcp.bat"));
                string removeMcpShContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "remove-mcp.sh"));

                AssertContains("private const string SourceMcpFramework = \"net10.0\";", mcpConfigHelperContents, "McpConfigHelper should pin source MCP installs to net10.0");
                AssertFalse(mcpConfigHelperContents.Contains("\"net8.0\""), "McpConfigHelper should not pin source MCP installs to net8.0");
                AssertContains("-f net10.0 -- mcp install --yes", installMcpBatContents, "install-mcp.bat should use net10.0");
                AssertContains("-f net10.0 -- mcp install --yes", installMcpShContents, "install-mcp.sh should use net10.0");
                AssertContains("-f net10.0 -- mcp remove --yes", removeMcpBatContents, "remove-mcp.bat should use net10.0");
                AssertContains("-f net10.0 -- mcp remove --yes", removeMcpShContents, "remove-mcp.sh should use net10.0");
            });

            await RunTest("Dashboard Mission TypeScript Model Includes Agent Output", () =>
            {
                string modelsContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Armada.Dashboard", "src", "types", "models.ts"));
                Match missionInterfaceMatch = Regex.Match(modelsContents, @"export interface Mission\s*\{(?<body>.*?)\n\}", RegexOptions.Singleline);

                AssertTrue(missionInterfaceMatch.Success, "Mission interface should exist in dashboard models");

                string missionInterfaceBody = missionInterfaceMatch.Groups["body"].Value;
                AssertContains("agentOutput?: string | null;", missionInterfaceBody, "Mission interface should expose nullable agent output");
                AssertContains("totalRuntimeMs: number | null;", missionInterfaceBody, "Mission interface should keep total runtime aligned with backend");
            });

            await RunTest("Dashboard Mission Detail Shows Agent Output In Collapsible Readonly Section", () =>
            {
                string missionDetailContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Armada.Dashboard", "src", "pages", "MissionDetail.tsx"));

                AssertContains("{mission.agentOutput && (", missionDetailContents, "Mission detail should render agent output conditionally");
                AssertContains("<details>", missionDetailContents, "Mission detail should use a collapsible details element for agent output");
                AssertContains(">Agent Output</summary>", missionDetailContents, "Mission detail should label the collapsible agent output section");
                AssertContains("gridColumn: '1 / -1'", missionDetailContents, "Agent output section should span the full detail grid width");
                AssertContains("overflow: 'auto'", missionDetailContents, "Agent output section should preserve scrollable overflow for long logs");
                AssertContains("maxHeight: '24rem'", missionDetailContents, "Agent output section should cap rendered height");
                AssertContains("fontFamily: 'monospace'", missionDetailContents, "Agent output section should preserve fixed-width log formatting");
                AssertContains(">{mission.agentOutput}</pre>", missionDetailContents, "Mission detail should render the raw agent output text");
            });

            await RunTest("Dashboard Mission Detail Keeps Current Layout And Avoids Legacy Patterns", () =>
            {
                string missionDetailContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Armada.Dashboard", "src", "pages", "MissionDetail.tsx"));

                AssertContains("gridTemplateColumns: '1fr 1fr 1fr 1fr'", missionDetailContents, "Mission detail should keep the current four-column grid");
                AssertFalse(missionDetailContents.Contains("gridTemplateColumns: '1fr 1fr 1fr'"), "Mission detail should not regress to the old three-column grid");
                AssertFalse(missionDetailContents.Contains("parsedTasks"), "Mission detail should not reference removed dispatch parsing state");
                AssertFalse(missionDetailContents.Contains("1 task detected"), "Mission detail should not include removed dispatch task detection copy");
            });
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "src")) &&
                    Directory.Exists(Path.Combine(current.FullName, "test")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
        }
    }
}
