namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors that pin the release surface to <see cref="Constants.ProductVersion"/>:
    /// shared build props, Helm entry point, MCP install scripts, Windows framework overrides,
    /// the status health route, and versioned docs/Postman examples. Positive cases assert the
    /// canonical version is wired through and stale literals are absent; negative cases assert
    /// scripts fail closed and the doc extractors throw when their anchors drift.
    /// </summary>
    public sealed class ReleaseVersionSuite : IArmadaTestSuite
    {
        #region Private-Members

        private static readonly string[] _StaleReleaseVersions = { "0.9.0", "0.8.0", "0.7.0", "0.6.0", "0.5.0", "0.4.0", "0.3.0" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the ReleaseVersion suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("product_version_and_shared_build_props_match_v090", "ProductVersion And Shared Build Props Match V090", TestTags.Positive, () =>
            {
                string repoRoot = FindRepositoryRoot();
                string propsContents = File.ReadAllText(Path.Combine(repoRoot, "src", "Directory.Build.props"));
                string helmProjectContents = File.ReadAllText(Path.Combine(repoRoot, "src", "Armada.Helm", "Armada.Helm.csproj"));
                string dashboardPackageContents = File.ReadAllText(Path.Combine(repoRoot, "src", "Armada.Dashboard", "package.json"));
                string dashboardLockContents = File.ReadAllText(Path.Combine(repoRoot, "src", "Armada.Dashboard", "package-lock.json"));
                MatchCollection versionMatches = Regex.Matches(propsContents, @"<Version>\s*([^<]+)\s*</Version>");

                AssertTrue(versionMatches.Count == 1, "Directory.Build.props should contain exactly one Version element");
                Match versionMatch = versionMatches[0];
                AssertEqual("1.0.0", Constants.ProductVersion);
                AssertEqual(Constants.ProductVersion, versionMatch.Groups[1].Value.Trim());
                AssertContains("<Version>" + Constants.ProductVersion + "</Version>", helmProjectContents, "Helm package version should align with the shared release version");
                AssertEqual(Constants.ProductVersion, JsonHelper.Deserialize<NpmPackageVersionView>(dashboardPackageContents).Version, "Dashboard package.json should align with the shared release version");
                NpmPackageVersionView dashboardLock = JsonHelper.Deserialize<NpmPackageVersionView>(dashboardLockContents);
                AssertEqual(Constants.ProductVersion, dashboardLock.Version, "Dashboard package-lock.json should align with the shared release version");
                AssertTrue(dashboardLock.Packages != null && dashboardLock.Packages.ContainsKey(""), "package-lock.json has a root package entry");
                AssertEqual(Constants.ProductVersion, dashboardLock.Packages![""].Version, "Dashboard package-lock.json root package should align with the shared release version");
            }));

            cases.Add(Case("helm_program_uses_product_version_constant", "Helm Program Uses ProductVersion Constant", TestTags.Positive, () =>
            {
                string programContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Armada.Helm", "Program.cs"));

                AssertContains("\"v\" + Constants.ProductVersion", programContents, "Helm banner/help version should come from Constants.ProductVersion");
                AssertContains("AnsiConsole.MarkupLine(\"[dim]Multi-Agent Orchestration System  \" + _VersionLabel + \"[/]\");", programContents, "Helm subtitle should render the shared version label");
                AssertContains("config.SetApplicationVersion(Constants.ProductVersion);", programContents, "Helm CLI version should come from Constants.ProductVersion");
                AssertFalse(programContents.Contains("0.3.0"), "Helm entry point should not contain the stale 0.3.0 literal");
                AssertFalse(programContents.Contains("\"1.0.0\""), "Helm entry point should not contain a hard-coded release version literal");
                AssertFalse(programContents.Contains("\"v1.0.0\""), "Helm entry point should compose the prefixed version instead of hard-coding it");
                AssertFalse(programContents.Contains("SetApplicationVersion(\"1.0.0\")"), "Helm CLI version should not be hard-coded");
            }));

            cases.Add(Case("source_mcp_helpers_use_net10_framework", "Source MCP Helpers Use Net10 Framework", TestTags.Positive, () =>
            {
                string mcpConfigHelperContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Armada.Helm", "Commands", "McpConfigHelper.cs"));
                string installMcpBatContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "windows", "install-mcp.bat"));
                string installMcpShContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "common", "install-mcp.sh"));
                string removeMcpBatContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "windows", "remove-mcp.bat"));
                string removeMcpShContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "common", "remove-mcp.sh"));
                string resolveFrameworkBatContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "windows", "resolve-framework.bat"));
                string resolveFrameworkShContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "common", "resolve-framework.sh"));

                AssertContains("private const string SourceMcpFramework = \"net10.0\";", mcpConfigHelperContents, "McpConfigHelper should pin source MCP installs to net10.0");
                AssertFalse(mcpConfigHelperContents.Contains("\"net8.0\""), "McpConfigHelper should not pin source MCP installs to net8.0");
                AssertContains("ARMADA_TARGET_FRAMEWORK=\"${ARMADA_TARGET_FRAMEWORK:-net10.0}\"", resolveFrameworkShContents, "Shell framework resolver should default to net10.0");
                AssertContains("export ARMADA_DOTNET_MSBUILD_FRAMEWORK_ARGS=", resolveFrameworkShContents, "Shell framework resolver should expose reusable msbuild framework arguments");
                AssertContains("source \"${SCRIPT_DIR}/resolve-framework.sh\"", installMcpShContents, "install-mcp.sh should resolve the framework override");
                AssertContains("-f \"$ARMADA_TARGET_FRAMEWORK\" -- mcp install --yes", installMcpShContents, "install-mcp.sh should honor the resolved framework override");
                AssertContains("source \"${SCRIPT_DIR}/resolve-framework.sh\"", removeMcpShContents, "remove-mcp.sh should resolve the framework override");
                AssertContains("-f \"$ARMADA_TARGET_FRAMEWORK\" -- mcp remove --yes", removeMcpShContents, "remove-mcp.sh should honor the resolved framework override");
                AssertContains("set \"FRAMEWORK=net10.0\"", resolveFrameworkBatContents, "Windows framework resolver should default to net10.0");
                AssertContains("set \"ARMADA_FORWARD_FRAMEWORK_ARGS=%FORWARD_ARGS%\"", resolveFrameworkBatContents, "Windows framework resolver should expose reusable wrapper forwarding arguments");
                AssertContains("set \"ARMADA_DOTNET_FRAMEWORK_ARGS=%DOTNET_FRAMEWORK_ARGS%\"", resolveFrameworkBatContents, "Windows framework resolver should expose reusable dotnet framework arguments");
                AssertContains("set \"ARMADA_DOTNET_MSBUILD_FRAMEWORK_ARGS=%DOTNET_MSBUILD_FRAMEWORK_ARGS%\"", resolveFrameworkBatContents, "Windows framework resolver should expose reusable msbuild framework arguments");
                AssertContains("call \"%SCRIPT_DIR%\\resolve-framework.bat\" %*", installMcpBatContents, "install-mcp.bat should resolve the Windows framework override");
                AssertContains("%ARMADA_DOTNET_FRAMEWORK_ARGS% -- mcp install --yes", installMcpBatContents, "install-mcp.bat should honor the resolved framework override");
                AssertContains("call \"%SCRIPT_DIR%\\resolve-framework.bat\" %*", removeMcpBatContents, "remove-mcp.bat should resolve the Windows framework override");
                AssertContains("%ARMADA_DOTNET_FRAMEWORK_ARGS% -- mcp remove --yes", removeMcpBatContents, "remove-mcp.bat should honor the resolved framework override");
            }));

            cases.Add(Case("windows_install_scripts_allow_explicit_framework_overrides", "Windows Install Scripts Allow Explicit Framework Overrides", TestTags.Positive, () =>
            {
                string installBatContents = ReadRepositoryFile("scripts", "windows", "install.bat");
                string reinstallBatContents = ReadRepositoryFile("scripts", "windows", "reinstall.bat");
                string removeBatContents = ReadRepositoryFile("scripts", "windows", "remove.bat");
                string updateBatContents = ReadRepositoryFile("scripts", "windows", "update.bat");
                string publishServerBatContents = ReadRepositoryFile("scripts", "windows", "publish-server.bat");
                string deployDashboardBatContents = ReadRepositoryFile("scripts", "windows", "deploy-dashboard.bat");
                string installWindowsTaskBatContents = ReadRepositoryFile("scripts", "windows", "install-windows-task.bat");
                string updateWindowsTaskBatContents = ReadRepositoryFile("scripts", "windows", "update-windows-task.bat");
                string removeWindowsTaskBatContents = ReadRepositoryFile("scripts", "windows", "remove-windows-task.bat");
                string healthcheckServerBatContents = ReadRepositoryFile("scripts", "windows", "healthcheck-server.bat");
                string serverStartCommandContents = ReadRepositoryFile("src", "Armada.Helm", "Commands", "ServerStartCommand.cs");

                AssertContains("call \"%SCRIPT_DIR%\\resolve-framework.bat\" %*", installBatContents, "install.bat should resolve framework overrides");
                AssertContains("dotnet build \"%REPO_ROOT%\\src\\Armada.sln\" %ARMADA_DOTNET_FRAMEWORK_ARGS%", installBatContents, "install.bat should build with the resolved framework");
                AssertContains("dotnet pack \"%REPO_ROOT%\\src\\Armada.Helm\\Armada.Helm.csproj\" %ARMADA_DOTNET_MSBUILD_FRAMEWORK_ARGS%", installBatContents, "install.bat should pack with the resolved framework");
                AssertContains("call \"%SCRIPT_DIR%\\resolve-framework.bat\" %*", reinstallBatContents, "reinstall.bat should resolve framework overrides");
                AssertContains("call \"%SCRIPT_DIR%\\install.bat\" %ARMADA_FORWARD_FRAMEWORK_ARGS%", reinstallBatContents, "reinstall.bat should forward the resolved framework explicitly");
                AssertContains("call \"%SCRIPT_DIR%\\resolve-framework.bat\" %*", removeBatContents, "remove.bat should accept framework overrides for parity");
                AssertContains("set \"HELM_DLL=%REPO_ROOT%\\src\\Armada.Helm\\bin\\Debug\\%ARMADA_TARGET_FRAMEWORK%\\Armada.Helm.dll\"", updateBatContents, "update.bat should probe the resolved Helm build output");
                AssertContains("call \"%SCRIPT_DIR%\\reinstall.bat\" %ARMADA_FORWARD_FRAMEWORK_ARGS%", updateBatContents, "update.bat should forward the resolved framework to reinstall explicitly");
                AssertContains("dotnet run --project \"%REPO_ROOT%\\src\\Armada.Helm\" %ARMADA_DOTNET_FRAMEWORK_ARGS% -- %*", updateBatContents, "update.bat should fall back to the resolved framework");
                AssertTrue(
                    updateBatContents.IndexOf("if exist \"%HELM_DLL%\"", StringComparison.Ordinal) < updateBatContents.IndexOf("where armada", StringComparison.Ordinal),
                    "update.bat should prefer repo-targeted Helm execution before the global tool");
                AssertContains("dotnet publish \"%REPO_ROOT%\\src\\Armada.Server\" -c Release %ARMADA_DOTNET_FRAMEWORK_ARGS%", publishServerBatContents, "publish-server.bat should publish with the resolved framework");
                AssertContains("set \"LOCAL_TSC=%LOCAL_BIN_DIR%\\tsc.cmd\"", deployDashboardBatContents, "deploy-dashboard.bat should resolve the local TypeScript compiler");
                AssertContains("set \"LOCAL_VITE=%LOCAL_BIN_DIR%\\vite.cmd\"", deployDashboardBatContents, "deploy-dashboard.bat should resolve the local Vite CLI");
                AssertContains("call \"%LOCAL_TSC%\"", deployDashboardBatContents, "deploy-dashboard.bat should build with the local TypeScript compiler instead of npm run");
                AssertContains("call \"%LOCAL_VITE%\" build", deployDashboardBatContents, "deploy-dashboard.bat should build with the local Vite CLI instead of npm run");
                AssertContains("if exist \"%USERPROFILE%\\.armada\\dashboard\\index.html\" (", publishServerBatContents, "publish-server.bat should only continue after dashboard deploy errors when a React dashboard is already deployed");
                AssertContains("echo ERROR: Dashboard deploy failed and no deployed React dashboard is available.", publishServerBatContents, "publish-server.bat should fail fresh installs when the React dashboard is unavailable");
                AssertContains("call \"%SCRIPT_DIR%\\publish-server.bat\" %ARMADA_FORWARD_FRAMEWORK_ARGS%", installWindowsTaskBatContents, "install-windows-task.bat should forward the resolved framework to publish-server explicitly");
                AssertContains("call \"%SCRIPT_DIR%\\install-windows-task.bat\" %ARMADA_FORWARD_FRAMEWORK_ARGS%", updateWindowsTaskBatContents, "update-windows-task.bat should forward the resolved framework explicitly");
                AssertContains("call \"%SCRIPT_DIR%\\resolve-framework.bat\" %*", removeWindowsTaskBatContents, "remove-windows-task.bat should accept framework overrides for parity");
                AssertContains("string targetFramework = GetCliTargetFramework();", serverStartCommandContents, "ServerStartCommand should derive the build framework from the running Helm target");
                AssertContains("buildInfo.ArgumentList.Add(targetFramework);", serverStartCommandContents, "ServerStartCommand should build Armada.Server with the derived framework");
                AssertContains("buildInfo.ArgumentList.Add(\"-p:TargetFramework=\" + targetFramework);", serverStartCommandContents, "ServerStartCommand should lock the server build to the derived framework");
                AssertContains("buildInfo.ArgumentList.Add(\"-p:TargetFrameworks=\" + targetFramework);", serverStartCommandContents, "ServerStartCommand should prevent multi-target evaluation on framework-limited machines");
                AssertContains("string tscPath = Path.Combine(localBinDir, \"tsc\" + toolExtension);", serverStartCommandContents, "ServerStartCommand should use the local TypeScript compiler");
                AssertContains("string vitePath = Path.Combine(localBinDir, \"vite\" + toolExtension);", serverStartCommandContents, "ServerStartCommand should use the local Vite CLI");
                AssertContains("Keeping the existing deployed React dashboard", serverStartCommandContents, "ServerStartCommand should preserve the existing React dashboard instead of silently downgrading to the legacy UI");
                AssertContains("return $\"net{parsed.Version.Major}.{parsed.Version.Minor}\";", serverStartCommandContents, "ServerStartCommand should map the runtime TFM to a dotnet framework moniker");
                AssertContains("if /I \"%~1\"==\"--framework\" set \"BASE_URL=%~3\"", healthcheckServerBatContents, "healthcheck-server.bat should ignore a leading named framework override");
                AssertContains("findstr /R /I \"^net[0-9][0-9]*\\.[0-9][0-9]*$\"", healthcheckServerBatContents, "healthcheck-server.bat should ignore a leading positional framework override");
            }));

            cases.Add(Case("windows_framework_resolver_accepts_positional_and_named_overrides", "Windows Framework Resolver Accepts Positional And Named Overrides", TestTags.Positive, () =>
            {
                if (!OperatingSystem.IsWindows())
                {
                    return;
                }

                string repoRoot = FindRepositoryRoot();
                string resolveFrameworkPath = Path.Combine(repoRoot, "scripts", "windows", "resolve-framework.bat");

                string positionalResult = RunCommandAndCaptureOutput(
                    "cmd.exe",
                    "/v:on /c call \"" + resolveFrameworkPath + "\" net8.0 >nul && echo !ARMADA_TARGET_FRAMEWORK!",
                    repoRoot);
                AssertEqual("net8.0", positionalResult.Trim(), "resolve-framework.bat should accept positional framework values");

                string namedResult = RunCommandAndCaptureOutput(
                    "cmd.exe",
                    "/v:on /c call \"" + resolveFrameworkPath + "\" --framework net8.0 >nul && echo !ARMADA_TARGET_FRAMEWORK!",
                    repoRoot);
                AssertEqual("net8.0", namedResult.Trim(), "resolve-framework.bat should accept named framework values");
            }));

            cases.Add(Case("windows_install_task_script_preserves_explicit_framework_end_to_end", "Windows Install Task Script Preserves Explicit Framework End To End", TestTags.Positive, () =>
            {
                if (!OperatingSystem.IsWindows())
                {
                    return;
                }

                string repoRoot = FindRepositoryRoot();
                string tempRoot = Path.Combine(Path.GetTempPath(), "armada-script-test-" + Guid.NewGuid().ToString("N"));
                string tempRepo = Path.Combine(tempRoot, "repo");
                string tempUserProfile = Path.Combine(tempRoot, "user");
                string toolsDir = Path.Combine(tempRoot, "tools");
                string logPath = Path.Combine(tempRoot, "dotnet.log");

                Directory.CreateDirectory(tempRepo);
                Directory.CreateDirectory(tempUserProfile);
                Directory.CreateDirectory(toolsDir);

                try
                {
                    string windowsScriptsDir = Path.Combine(tempRepo, "scripts", "windows");
                    Directory.CreateDirectory(windowsScriptsDir);
                    CopyRepositoryFile(repoRoot, tempRepo, Path.Combine("scripts", "windows", "resolve-framework.bat"));
                    CopyRepositoryFile(repoRoot, tempRepo, Path.Combine("scripts", "windows", "publish-server.bat"));
                    CopyRepositoryFile(repoRoot, tempRepo, Path.Combine("scripts", "windows", "deploy-dashboard.bat"));
                    CopyRepositoryFile(repoRoot, tempRepo, Path.Combine("scripts", "windows", "install-windows-task.bat"));
                    CopyRepositoryFile(repoRoot, tempRepo, Path.Combine("scripts", "windows", "healthcheck-server.bat"));
                    CopyRepositoryFile(repoRoot, tempRepo, Path.Combine("scripts", "windows", "start-armada-server.ps1"));
                    CopyRepositoryFile(repoRoot, tempRepo, Path.Combine("scripts", "windows", "stop-armada-server.ps1"));

                    string dashboardDir = Path.Combine(tempRepo, "src", "Armada.Dashboard");
                    Directory.CreateDirectory(dashboardDir);
                    File.WriteAllText(Path.Combine(dashboardDir, "package.json"), "{}");
                    Directory.CreateDirectory(Path.Combine(dashboardDir, "node_modules", ".bin"));
                    File.WriteAllText(
                        Path.Combine(dashboardDir, "node_modules", ".bin", "tsc.cmd"),
                        "@echo off\r\n" +
                        "exit /b 0\r\n");
                    File.WriteAllText(
                        Path.Combine(dashboardDir, "node_modules", ".bin", "vite.cmd"),
                        "@echo off\r\n" +
                        "set \"DIST_DIR=%REPO_ROOT_FOR_TEST%\\src\\Armada.Dashboard\\dist\"\r\n" +
                        "mkdir \"%DIST_DIR%\" >nul 2>nul\r\n" +
                        "type nul > \"%DIST_DIR%\\index.html\"\r\n" +
                        "exit /b 0\r\n");

                    File.WriteAllText(
                        Path.Combine(toolsDir, "dotnet.cmd"),
                        "@echo off\r\n" +
                        "echo %*>>\"%DOTNET_LOG%\"\r\n" +
                        "if /I \"%~1\"==\"publish\" call :publish %*\r\n" +
                        "exit /b 0\r\n" +
                        "\r\n" +
                        ":publish\r\n" +
                        "set \"OUTPUT_DIR=\"\r\n" +
                        ":publish_args\r\n" +
                        "if \"%~1\"==\"\" goto publish_done\r\n" +
                        "if /I \"%~1\"==\"-o\" (\r\n" +
                        "  set \"OUTPUT_DIR=%~2\"\r\n" +
                        "  shift\r\n" +
                        ")\r\n" +
                        "shift\r\n" +
                        "goto publish_args\r\n" +
                        "\r\n" +
                        ":publish_done\r\n" +
                        "if \"%OUTPUT_DIR%\"==\"\" exit /b 1\r\n" +
                        "mkdir \"%OUTPUT_DIR%\" >nul 2>nul\r\n" +
                        "type nul > \"%OUTPUT_DIR%\\Armada.Server.exe\"\r\n" +
                        "exit /b 0\r\n");

                    File.WriteAllText(
                        Path.Combine(toolsDir, "npm.cmd"),
                        "@echo off\r\n" +
                        "exit /b 1\r\n");

                    File.WriteAllText(
                        Path.Combine(toolsDir, "powershell.cmd"),
                        "@echo off\r\n" +
                        "exit /b 0\r\n");

                    string installTaskPath = Path.Combine(tempRepo, "scripts", "windows", "install-windows-task.bat");
                    RunCommandAndCaptureOutput(
                        "cmd.exe",
                        "/c call \"" + installTaskPath + "\" net8.0",
                        tempRepo,
                        new Dictionary<string, string>
                        {
                            ["PATH"] = toolsDir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
                            ["USERPROFILE"] = tempUserProfile,
                            ["DOTNET_LOG"] = logPath,
                            ["REPO_ROOT_FOR_TEST"] = tempRepo
                        });

                    string dotnetLogContents = File.ReadAllText(logPath);
                    AssertContains("publish \"" + Path.Combine(tempRepo, "src", "Armada.Server") + "\" -c Release --framework net8.0 -p:TargetFramework=net8.0 -p:TargetFrameworks=net8.0 -o \"" + Path.Combine(tempUserProfile, ".armada", "bin") + "\"", dotnetLogContents, "install-windows-task.bat should preserve the explicit framework into publish-server.bat");
                    AssertFalse(dotnetLogContents.Contains("net10.0", StringComparison.Ordinal), "install-windows-task.bat should not fall back to net10.0 when net8.0 is requested");
                }
                finally
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                    }
                }
            }));

            cases.Add(Case("windows_publish_server_script_fails_when_react_dashboard_is_unavailable", "Windows Publish Server Script Fails When React Dashboard Is Unavailable", TestTags.Negative, () =>
            {
                if (!OperatingSystem.IsWindows())
                {
                    return;
                }

                string repoRoot = FindRepositoryRoot();
                string tempRoot = Path.Combine(Path.GetTempPath(), "armada-publish-test-" + Guid.NewGuid().ToString("N"));
                string tempRepo = Path.Combine(tempRoot, "repo");
                string tempUserProfile = Path.Combine(tempRoot, "user");
                string toolsDir = Path.Combine(tempRoot, "tools");
                string logPath = Path.Combine(tempRoot, "dotnet.log");

                Directory.CreateDirectory(tempRepo);
                Directory.CreateDirectory(tempUserProfile);
                Directory.CreateDirectory(toolsDir);

                try
                {
                    CopyRepositoryFile(repoRoot, tempRepo, Path.Combine("scripts", "windows", "resolve-framework.bat"));
                    CopyRepositoryFile(repoRoot, tempRepo, Path.Combine("scripts", "windows", "publish-server.bat"));
                    CopyRepositoryFile(repoRoot, tempRepo, Path.Combine("scripts", "windows", "deploy-dashboard.bat"));

                    string dashboardDir = Path.Combine(tempRepo, "src", "Armada.Dashboard");
                    Directory.CreateDirectory(dashboardDir);
                    File.WriteAllText(Path.Combine(dashboardDir, "package.json"), "{}");
                    File.WriteAllText(Path.Combine(dashboardDir, "package-lock.json"), "{}");

                    File.WriteAllText(
                        Path.Combine(toolsDir, "dotnet.cmd"),
                        "@echo off\r\n" +
                        "echo %*>>\"%DOTNET_LOG%\"\r\n" +
                        "if /I \"%~1\"==\"publish\" call :publish %*\r\n" +
                        "exit /b 0\r\n" +
                        "\r\n" +
                        ":publish\r\n" +
                        "set \"OUTPUT_DIR=\"\r\n" +
                        ":publish_args\r\n" +
                        "if \"%~1\"==\"\" goto publish_done\r\n" +
                        "if /I \"%~1\"==\"-o\" (\r\n" +
                        "  set \"OUTPUT_DIR=%~2\"\r\n" +
                        "  shift\r\n" +
                        ")\r\n" +
                        "shift\r\n" +
                        "goto publish_args\r\n" +
                        "\r\n" +
                        ":publish_done\r\n" +
                        "if \"%OUTPUT_DIR%\"==\"\" exit /b 1\r\n" +
                        "mkdir \"%OUTPUT_DIR%\" >nul 2>nul\r\n" +
                        "type nul > \"%OUTPUT_DIR%\\Armada.Server.exe\"\r\n" +
                        "exit /b 0\r\n");

                    File.WriteAllText(
                        Path.Combine(toolsDir, "npm.cmd"),
                        "@echo off\r\n" +
                        "exit /b 1\r\n");

                    string publishServerPath = Path.Combine(tempRepo, "scripts", "windows", "publish-server.bat");
                    Exception? captured = null;
                    try
                    {
                        RunCommandAndCaptureOutput(
                            "cmd.exe",
                            "/c call \"" + publishServerPath + "\" net8.0",
                            tempRepo,
                            new Dictionary<string, string>
                            {
                                ["PATH"] = toolsDir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
                                ["USERPROFILE"] = tempUserProfile,
                                ["DOTNET_LOG"] = logPath
                            });
                    }
                    catch (Exception ex)
                    {
                        captured = ex;
                    }

                    AssertTrue(captured != null, "publish-server.bat should fail when React dashboard deployment fails on a fresh install");
                    AssertContains("Dashboard deploy failed and no deployed React dashboard is available.", captured!.Message, "publish-server.bat should report that the React dashboard is required for a fresh install");
                }
                finally
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                    }
                }
            }));

            cases.Add(Case("status_health_route_uses_product_version_constant", "Status Health Route Uses ProductVersion Constant", TestTags.Positive, () =>
            {
                string statusRoutesContents = ReadRepositoryFile("src", "Armada.Server", "Routes", "StatusRoutes.cs");
                Match healthRouteMatch = Regex.Match(
                    statusRoutesContents,
                    @"app(?:\.Rest)?\.Get\(""/api/v1/status/health"",[\s\S]*?\.WithDescription\(""Returns health status\. Does not require authentication\.""\)\);");

                AssertTrue(healthRouteMatch.Success, "StatusRoutes should include the REST health route");

                string healthRouteBlock = healthRouteMatch.Value;
                AssertContains("Version = ArmadaConstants.ProductVersion,", healthRouteBlock, "Health route should return ArmadaConstants.ProductVersion");
                AssertFalse(
                    healthRouteBlock.Contains("Version = \"" + Constants.ProductVersion + "\"", StringComparison.Ordinal),
                    "Health route should not hard-code the canonical release version");

                AssertNoStaleVersionSurfaces(
                    healthRouteBlock,
                    "StatusRoutes health route",
                    staleVersion => "Version = \"" + staleVersion + "\"");
            }));

            cases.Add(Case("versioned_docs_and_examples_match_product_version", "Versioned Docs And Examples Match ProductVersion", TestTags.Positive, () =>
            {
                string restApiContents = ReadRepositoryFile("docs", "REST_API.md");
                string mcpApiContents = ReadRepositoryFile("docs", "MCP_API.md");
                string proxyApiContents = ReadRepositoryFile("docs", "PROXY_API.md");
                string postmanContents = ReadRepositoryFile("Armada.postman_collection.json");
                string restHealthSample = ExtractRestHealthResponseSample(restApiContents);
                string restJsonExamples = ExtractMarkdownJsonExamples(restApiContents);
                string mcpJsonExamples = ExtractMarkdownJsonExamples(mcpApiContents);
                string proxyJsonExamples = ExtractMarkdownJsonExamples(proxyApiContents);
                string postmanResponseBodies = ExtractPostmanResponseBodies(postmanContents);
                string postmanHealthResponseBody = ExtractPostmanHealthyResponseBody(postmanContents);

                AssertEqual(1, Regex.Matches(restApiContents, @"#### GET /api/v1/status/health").Count, "REST API should document the health endpoint exactly once");
                AssertEqual(1, Regex.Matches(postmanContents, @"""raw"":\s*""\{\{baseUrl\}\}/api/v1/status/health""").Count, "Postman collection should include the health request exactly once");
                AssertEqual(1, Regex.Matches(postmanContents, @"""name"":\s*""Healthy""").Count, "Postman collection should include the healthy response example exactly once");
                AssertContains("**Version:** " + Constants.ProductVersion, restApiContents, "REST API header should use the shared release version");
                AssertContains("**Version:** " + Constants.ProductVersion, mcpApiContents, "MCP API header should use the shared release version");
                AssertContains("**Version:** " + Constants.ProductVersion, proxyApiContents, "Proxy API header should use the shared release version");
                AssertContains("Version: " + Constants.ProductVersion, postmanContents, "Postman collection description should use the shared release version");
                AssertEqual(Constants.ProductVersion, JsonHelper.Deserialize<UpgradeHealthResponse>(restHealthSample).Version, "REST API health example should use the shared release version");
                AssertEqual(Constants.ProductVersion, JsonHelper.Deserialize<UpgradeHealthResponse>(postmanHealthResponseBody).Version, "Postman health response body should use the shared release version");

                AssertNoStaleVersionSurfaces(restJsonExamples, "REST API JSON examples", staleVersion => staleVersion);
                AssertNoStaleVersionSurfaces(mcpJsonExamples, "MCP API JSON examples", staleVersion => staleVersion);
                AssertNoStaleVersionSurfaces(proxyJsonExamples, "Proxy API JSON examples", staleVersion => staleVersion);
                AssertNoStaleVersionSurfaces(postmanResponseBodies, "Postman response bodies", staleVersion => staleVersion);
                AssertNoStaleVersionSurfaces(restHealthSample, "REST API health example", staleVersion => "\"Version\": \"" + staleVersion + "\"");
                AssertNoStaleVersionSurfaces(postmanHealthResponseBody, "Postman health response body", staleVersion => "\"Version\": \"" + staleVersion + "\"");
            }));

            cases.Add(Case("release_surface_extractors_fail_closed_when_health_samples_drift", "Release Surface Extractors Fail Closed When Health Samples Drift", TestTags.Negative, () =>
            {
                string restApiContents = ReadRepositoryFile("docs", "REST_API.md");
                string postmanContents = ReadRepositoryFile("Armada.postman_collection.json");

                AssertThrows<Exception>(
                    () => ExtractRestHealthResponseSample(
                        restApiContents.Replace("#### GET /api/v1/status/health", "#### GET /api/v1/status", StringComparison.Ordinal)),
                    "REST API extractor should fail when the health section heading is missing");
                AssertThrows<Exception>(
                    () => ExtractPostmanHealthyResponseBody(
                        Regex.Replace(postmanContents, "\"name\":\\s*\"Healthy\"", "\"name\": \"HealthyExample\"")),
                    "Postman extractor should fail when the healthy response example is missing");
                AssertThrows<Exception>(
                    () => AssertNoStaleVersionSurfaces("Version: 0.4.0", "synthetic release surface", staleVersion => staleVersion),
                    "Stale release helper should fail when a prior version literal is present");
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.ReleaseVersion",
                displayName: "Release Version",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "src")) &&
                    Directory.Exists(Path.Combine(current.FullName, "docs")) &&
                    File.Exists(Path.Combine(current.FullName, "README.md")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
        }

        private static void AssertNoStaleVersionSurfaces(string contents, string surfaceName, Func<string, string> staleSurfaceFactory)
        {
            foreach (string staleVersion in _StaleReleaseVersions)
            {
                string staleSurface = staleSurfaceFactory(staleVersion);
                AssertFalse(
                    contents.Contains(staleSurface, StringComparison.Ordinal),
                    surfaceName + " should not contain stale release literal " + staleSurface);
            }
        }

        private static string ReadRepositoryFile(params string[] relativePath)
        {
            return File.ReadAllText(Path.Combine(FindRepositoryRoot(), Path.Combine(relativePath)));
        }

        private static void CopyRepositoryFile(string repoRoot, string tempRepo, string relativePath)
        {
            string sourcePath = Path.Combine(repoRoot, relativePath);
            string destinationPath = Path.Combine(tempRepo, relativePath);
            string? destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(sourcePath, destinationPath, overwrite: true);
        }

        private static string RunCommandAndCaptureOutput(string fileName, string arguments, string workingDirectory, IDictionary<string, string>? environmentVariables = null)
        {
            using Process process = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (environmentVariables != null)
            {
                foreach (KeyValuePair<string, string> variable in environmentVariables)
                {
                    startInfo.Environment[variable.Key] = variable.Value;
                }
            }

            process.StartInfo = startInfo;

            process.Start();
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new Exception("Command failed: " + fileName + " " + arguments + Environment.NewLine + stdout + stderr);
            }

            return stdout;
        }

        private static string ExtractRestHealthResponseSample(string restApiContents)
        {
            Match match = Regex.Match(
                restApiContents,
                @"#### GET /api/v1/status/health[\s\S]*?```json\s*(?<body>\{[\s\S]*?\})\s*```");

            if (!match.Success)
            {
                throw new Exception("Could not locate the REST API health response sample.");
            }

            return match.Groups["body"].Value;
        }

        private static string ExtractMarkdownJsonExamples(string markdownContents)
        {
            MatchCollection matches = Regex.Matches(
                markdownContents,
                @"```json\s*(?<body>[\s\S]*?)\s*```");

            if (matches.Count == 0)
            {
                throw new Exception("Could not locate any markdown JSON examples.");
            }

            System.Text.StringBuilder combined = new System.Text.StringBuilder();
            foreach (Match match in matches)
            {
                if (combined.Length > 0)
                {
                    combined.Append('\n');
                }

                combined.Append(match.Groups["body"].Value);
            }

            return combined.ToString();
        }

        private static string ExtractPostmanResponseBodies(string postmanContents)
        {
            MatchCollection matches = Regex.Matches(
                postmanContents,
                @"""body"":\s*""(?<body>(?:\\.|[^""\\])*)""");

            if (matches.Count == 0)
            {
                throw new Exception("Could not locate any Postman response bodies.");
            }

            System.Text.StringBuilder combined = new System.Text.StringBuilder();
            foreach (Match match in matches)
            {
                if (combined.Length > 0)
                {
                    combined.Append('\n');
                }

                combined.Append(Regex.Unescape(match.Groups["body"].Value));
            }

            return combined.ToString();
        }

        private static string ExtractPostmanHealthyResponseBody(string postmanContents)
        {
            Match match = Regex.Match(
                postmanContents,
                @"""name"":\s*""Healthy""[\s\S]*?""body"":\s*""(?<body>(?:\\.|[^""\\])*)""");

            if (!match.Success)
            {
                throw new Exception("Could not locate the Postman health response body.");
            }

            return Regex.Unescape(match.Groups["body"].Value);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.ReleaseVersion",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.ReleaseVersion",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
