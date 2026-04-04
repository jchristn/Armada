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

            await RunTest("Dashboard Login Flow Footer Keeps GitHub And Version Before Theme Toggle", () =>
            {
                string loginFlowContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Armada.Dashboard", "src", "components", "LoginFlow.tsx"));
                string loginFooterContents = ExtractMarkupSection(loginFlowContents, "<div className=\"login-footer\">", "</div>");

                int githubLinkIndex = loginFooterContents.IndexOf("className=\"github-link\"", StringComparison.Ordinal);
                int versionIndex = loginFooterContents.IndexOf("__APP_VERSION__", StringComparison.Ordinal);
                int themeToggleIndex = loginFooterContents.IndexOf("className=\"login-theme-toggle\"", StringComparison.Ordinal);

                AssertTrue(githubLinkIndex >= 0, "Login footer should keep the GitHub link");
                AssertTrue(versionIndex >= 0, "Login footer should keep the version label");
                AssertTrue(themeToggleIndex >= 0, "Login footer should keep the theme toggle");
                AssertTrue(githubLinkIndex < versionIndex, "GitHub link should remain before the version label");
                AssertTrue(versionIndex < themeToggleIndex, "Version label should remain before the theme toggle so mobile ordering can restack without markup changes");
            });

            await RunTest("Dashboard Login Footer Applies Mobile Wrap Without Desktop Regression", () =>
            {
                string appCssContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Armada.Dashboard", "src", "App.css"));
                string mobileCssContents = ExtractBalancedBlock(appCssContents, "@media (max-width: 768px)");
                string desktopFooterContents = ExtractBalancedBlock(appCssContents, ".login-footer {", true);
                string desktopThemeToggleContents = ExtractBalancedBlock(appCssContents, ".login-theme-toggle {", true);
                string mobileFooterContents = ExtractBalancedBlock(mobileCssContents, ".login-footer {");
                string mobileGithubLinkContents = ExtractBalancedBlock(mobileCssContents, ".login-footer .github-link {");
                string mobileVersionContents = ExtractBalancedBlock(mobileCssContents, ".login-footer span {");
                string mobileThemeToggleContents = ExtractBalancedBlock(mobileCssContents, ".login-theme-toggle {");

                AssertContains("justify-content: space-between;", desktopFooterContents, "Desktop login footer should keep the three-way layout");
                AssertContains("border-top: 1px solid var(--border);", desktopFooterContents, "Desktop login footer should keep the original separator");
                AssertContains("border: 1px solid var(--border);", desktopThemeToggleContents, "Desktop theme toggle should keep its bordered button styling");
                AssertFalse(desktopThemeToggleContents.Contains("width: 100%;"), "Desktop theme toggle should not stretch full width");

                AssertContains("flex-wrap: wrap;", mobileFooterContents, "Mobile login footer should wrap to a second row");
                AssertContains("justify-content: center;", mobileFooterContents, "Mobile login footer should center the first row");
                AssertContains("gap: 12px;", mobileFooterContents, "Mobile login footer should keep consistent spacing when wrapped");
                AssertFalse(mobileFooterContents.Contains("space-between"), "Mobile login footer should not keep the desktop spacing rule");
                AssertContains("order: 1;", mobileGithubLinkContents, "Mobile GitHub link should stay first in the wrapped footer");
                AssertContains("order: 2;", mobileVersionContents, "Mobile version label should stay on the first row after the GitHub link");
                AssertContains("order: 3;", mobileThemeToggleContents, "Mobile theme toggle should move after the footer text row");
                AssertContains("width: 100%;", mobileThemeToggleContents, "Mobile theme toggle should take its own centered row");
                AssertContains("display: flex;", mobileThemeToggleContents, "Mobile theme toggle should use flex centering");
                AssertContains("justify-content: center;", mobileThemeToggleContents, "Mobile theme toggle should center its contents");
                AssertContains("border: none;", mobileThemeToggleContents, "Mobile theme toggle should clear the desktop button border");
                AssertContains("padding-top: 8px;", mobileThemeToggleContents, "Mobile theme toggle should add top spacing below the footer text");
                AssertContains("margin-top: 4px;", mobileThemeToggleContents, "Mobile theme toggle should separate from the first footer row");
                AssertContains("border-top: none;", mobileThemeToggleContents, "Mobile theme toggle should not draw an extra separator");
                AssertFalse(mobileThemeToggleContents.Contains("border: 1px solid var(--border);"), "Mobile theme toggle should not keep the desktop border override");
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

        private static string ExtractMarkupSection(string contents, string startToken, string endToken)
        {
            int startIndex = contents.IndexOf(startToken, StringComparison.Ordinal);
            if (startIndex < 0)
            {
                throw new InvalidDataException($"Could not find markup section starting with '{startToken}'.");
            }

            int endIndex = contents.IndexOf(endToken, startIndex, StringComparison.Ordinal);
            if (endIndex < 0)
            {
                throw new InvalidDataException($"Could not find markup section ending with '{endToken}'.");
            }

            return contents.Substring(startIndex, endIndex - startIndex + endToken.Length);
        }

        private static string ExtractBalancedBlock(string contents, string header, bool useLastOccurrence = false)
        {
            int headerIndex = useLastOccurrence
                ? contents.LastIndexOf(header, StringComparison.Ordinal)
                : contents.IndexOf(header, StringComparison.Ordinal);

            if (headerIndex < 0)
            {
                throw new InvalidDataException($"Could not find block starting with '{header}'.");
            }

            int openBraceIndex = contents.IndexOf('{', headerIndex);
            if (openBraceIndex < 0)
            {
                throw new InvalidDataException($"Could not find opening brace for '{header}'.");
            }

            int depth = 0;
            for (int i = openBraceIndex; i < contents.Length; i++)
            {
                if (contents[i] == '{')
                {
                    depth++;
                }
                else if (contents[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return contents.Substring(headerIndex, i - headerIndex + 1);
                    }
                }
            }

            throw new InvalidDataException($"Could not find closing brace for '{header}'.");
        }
    }
}
