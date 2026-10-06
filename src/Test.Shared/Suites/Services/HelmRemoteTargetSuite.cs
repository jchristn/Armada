namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Helm;
    using Armada.Helm.Commands;
    using Armada.Helm.Infrastructure;
    using Armada.Tui.Services;
    using Armada.Tui.Services.Credentials;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The CLI's Admiral targeting: resolution order (--server/--profile, ARMADA_SERVER_URL, the active profile shared
    /// with the TUI, the local Admiral), the credential sent, refusal of local-only commands for a remote target, no
    /// embedded server for a remote target, clear 401 errors, and <c>armada mcp install</c> configs for a remote
    /// Admiral. The CLI used to always call http://127.0.0.1:admiralPort and silently ignore --server.
    /// </summary>
    public sealed class HelmRemoteTargetSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HelmRemoteTarget";
        private const int LocalPort = 23277;
        private const string LocalKey = "local-api-key";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("resolution_order_flag_env_profile_local", "Target resolution: flag, then ARMADA_SERVER_URL, then the active profile, then the local Admiral", TestTags.Positive, async () =>
            {
                Dictionary<string, string?> env = new Dictionary<string, string?>();
                ProfileFixture fx = await ProfileFixture.CreateAsync("prod", "https://prod.example.com", "stored-token", active: true).ConfigureAwait(false);
                AdmiralTargetResolver resolver = fx.Resolver(env);

                AdmiralTarget profile = await resolver.ResolveAsync(new AdmiralTargetRequest()).ConfigureAwait(false);
                AssertEqual(AdmiralTargetSourceEnum.Profile, profile.Source);
                AssertEqual("https://prod.example.com", profile.BaseUrl);
                AssertEqual("stored-token", profile.Token);
                AssertEqual(AdmiralCredentialSourceEnum.ProfileStore, profile.CredentialSource);
                AssertFalse(profile.IsLocal, "a remote profile is not local");

                env[AdmiralTargetResolver.ServerUrlEnvironmentVariable] = "https://env.example.com/";
                env[AdmiralTargetResolver.TokenEnvironmentVariable] = "env-token";
                AdmiralTarget fromEnv = await resolver.ResolveAsync(new AdmiralTargetRequest()).ConfigureAwait(false);
                AssertEqual(AdmiralTargetSourceEnum.Environment, fromEnv.Source);
                AssertEqual("https://env.example.com", fromEnv.BaseUrl, "trailing slash is trimmed");
                AssertEqual("env-token", fromEnv.Token);

                AdmiralTarget fromFlag = await resolver.ResolveAsync(new AdmiralTargetRequest { Server = "http://10.1.2.3:7890", Token = "flag-token" }).ConfigureAwait(false);
                AssertEqual(AdmiralTargetSourceEnum.Flag, fromFlag.Source);
                AssertEqual("http://10.1.2.3:7890", fromFlag.BaseUrl);
                AssertEqual("flag-token", fromFlag.Token);
                AssertEqual(AdmiralCredentialSourceEnum.Flag, fromFlag.CredentialSource);

                AdmiralTarget profileFlag = await resolver.ResolveAsync(new AdmiralTargetRequest { Profile = "PROD" }).ConfigureAwait(false);
                AssertEqual("https://prod.example.com", profileFlag.BaseUrl, "--profile beats ARMADA_SERVER_URL");
                AssertEqual("env-token", profileFlag.Token, "ARMADA_TOKEN beats the stored token");

                // The legacy TUI variable is honored when ARMADA_SERVER_URL is unset.
                env.Remove(AdmiralTargetResolver.ServerUrlEnvironmentVariable);
                env[AdmiralTargetResolver.LegacyServerUrlEnvironmentVariable] = "https://legacy.example.com";
                AdmiralTarget legacy = await resolver.ResolveAsync(new AdmiralTargetRequest()).ConfigureAwait(false);
                AssertEqual("https://legacy.example.com", legacy.BaseUrl);

                // No flag, no environment, no remote profile: the local Admiral with the local API key, as before.
                ProfileFixture empty = await ProfileFixture.CreateAsync(null, null, null, active: false).ConfigureAwait(false);
                Dictionary<string, string?> tokenOnly = new Dictionary<string, string?> { [AdmiralTargetResolver.TokenEnvironmentVariable] = "ignored-for-local" };
                AdmiralTarget local = await empty.Resolver(tokenOnly).ResolveAsync(new AdmiralTargetRequest()).ConfigureAwait(false);
                AssertEqual(AdmiralTargetSourceEnum.LocalDefault, local.Source);
                AssertEqual("http://127.0.0.1:" + LocalPort, local.BaseUrl);
                AssertTrue(local.IsLocal);
                AssertEqual(LocalKey, local.ApiKey);
                AssertNull(local.Token, "ARMADA_TOKEN does not change the local default credential");
            }));

            cases.Add(CaseAsync("local_profile_and_local_url_are_local", "'local', a profile following the local Admiral, and a loopback URL on admiralPort all target this machine", TestTags.Positive, async () =>
            {
                ProfileFixture fx = await ProfileFixture.CreateAsync("prod", "https://prod.example.com", "stored-token", active: true).ConfigureAwait(false);
                AdmiralTargetResolver resolver = fx.Resolver(new Dictionary<string, string?>());
                AdmiralTarget viaLocal = await resolver.ResolveAsync(new AdmiralTargetRequest { Profile = "local" }).ConfigureAwait(false);
                AssertTrue(viaLocal.IsLocal);
                AssertEqual(LocalKey, viaLocal.ApiKey);

                AdmiralTarget loopback = await resolver.ResolveAsync(new AdmiralTargetRequest { Server = "http://localhost:" + LocalPort }).ConfigureAwait(false);
                AssertTrue(loopback.IsLocal, "loopback URL on the local port is the local Admiral");
                AssertEqual(LocalKey, loopback.ApiKey);

                AdmiralTarget otherPort = await resolver.ResolveAsync(new AdmiralTargetRequest { Server = "http://127.0.0.1:" + (LocalPort + 1) }).ConfigureAwait(false);
                AssertFalse(otherPort.IsLocal, "another port is another Admiral");
                AssertNull(otherPort.ApiKey, "the local API key never goes to another Admiral");

                ProfileFixture following = await ProfileFixture.CreateAsync("default", "http://127.0.0.1:7890", null, active: true).ConfigureAwait(false);
                AdmiralTarget follows = await following.Resolver(new Dictionary<string, string?>()).ResolveAsync(new AdmiralTargetRequest()).ConfigureAwait(false);
                AssertEqual(AdmiralTargetSourceEnum.LocalDefault, follows.Source, "the TUI's auto-created local profile means the local Admiral");
            }));

            cases.Add(CaseAsync("invalid_targets_are_typed_errors", "Bad URLs, unknown profiles, and --server with --profile are typed errors", TestTags.Negative, async () =>
            {
                ProfileFixture fx = await ProfileFixture.CreateAsync(null, null, null, active: false).ConfigureAwait(false);
                AdmiralTargetResolver resolver = fx.Resolver(new Dictionary<string, string?>());
                foreach (string bad in new[] { "ftp://x", "armada.example.com", "/relative", "http://host:7890/?q=1", "http://" })
                {
                    AdmiralTargetException ex = await ExpectTargetErrorAsync(() => resolver.ResolveAsync(new AdmiralTargetRequest { Server = bad })).ConfigureAwait(false);
                    AssertEqual(AdmiralTargetErrorEnum.InvalidServerUrl, ex.ErrorCode, bad);
                }

                AdmiralTargetException unknown = await ExpectTargetErrorAsync(() => resolver.ResolveAsync(new AdmiralTargetRequest { Profile = "nope" })).ConfigureAwait(false);
                AssertEqual(AdmiralTargetErrorEnum.UnknownProfile, unknown.ErrorCode);

                AdmiralTargetException both = await ExpectTargetErrorAsync(() => resolver.ResolveAsync(new AdmiralTargetRequest { Server = "https://a.example.com", Profile = "x" })).ConfigureAwait(false);
                AssertEqual(AdmiralTargetErrorEnum.ConflictingOptions, both.ErrorCode);

                AdmiralTargetResolver badEnv = fx.Resolver(new Dictionary<string, string?> { [AdmiralTargetResolver.ServerUrlEnvironmentVariable] = "not a url" });
                AdmiralTargetException envError = await ExpectTargetErrorAsync(() => badEnv.ResolveAsync(new AdmiralTargetRequest())).ConfigureAwait(false);
                AssertEqual(AdmiralTargetErrorEnum.InvalidServerUrl, envError.ErrorCode);
                AssertContains(AdmiralTargetResolver.ServerUrlEnvironmentVariable, envError.Message, "names the variable");
            }));

            cases.Add(Case("credential_headers_and_transport_warning", "A token goes as Bearer, X-Token, and X-Api-Key; the local key alone as X-Api-Key; plain HTTP to a non-loopback host is flagged", TestTags.Positive, () =>
            {
                AdmiralTarget remote = new AdmiralTarget("http://10.0.0.5:7890", AdmiralTargetSourceEnum.Flag, null, false, "tok", null, AdmiralCredentialSourceEnum.Flag);
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "http://x/"))
                {
                    remote.ApplyCredentials(request.Headers);
                    AssertEqual("Bearer tok", request.Headers.Authorization?.ToString());
                    AssertEqual("tok", request.Headers.GetValues("X-Token").Single());
                    AssertEqual("tok", request.Headers.GetValues("X-Api-Key").Single());
                }

                AssertTrue(remote.SendsCredentialInsecurely, "http to 10.0.0.5 with a token");
                AssertFalse(new AdmiralTarget("https://a.example.com", AdmiralTargetSourceEnum.Flag, null, false, "tok", null, AdmiralCredentialSourceEnum.Flag).SendsCredentialInsecurely, "https is fine");
                AssertFalse(new AdmiralTarget("http://10.0.0.5:7890", AdmiralTargetSourceEnum.Flag, null, false, null, null, AdmiralCredentialSourceEnum.None).SendsCredentialInsecurely, "no credential, nothing to leak");

                AdmiralTarget local = new AdmiralTarget("http://127.0.0.1:7890", AdmiralTargetSourceEnum.LocalDefault, null, true, null, "key", AdmiralCredentialSourceEnum.LocalApiKey);
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "http://x/"))
                {
                    local.ApplyCredentials(request.Headers);
                    AssertNull(request.Headers.Authorization);
                    AssertEqual("key", request.Headers.GetValues("X-Api-Key").Single());
                }

                AssertFalse(local.SendsCredentialInsecurely, "loopback");
                AssertFalse(remote.Describe().Contains("tok"), "descriptions never contain the token");
            }));

            cases.Add(CaseAsync("remote_command_calls_server_with_bearer", "mission list --server --token sends its requests to that URL with the bearer token, not to the local Admiral", TestTags.Positive, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    int exit = await RunCliAsync("mission", "list", "--server", stub.BaseUrl, "--token", "bearer-123").ConfigureAwait(false);
                    AssertEqual(0, exit);
                    List<RecordedAdmiralRequest> requests = stub.Requests();
                    AssertTrue(requests.Any(r => r.Path == "/api/v1/missions"), "mission list reached the --server URL");
                    foreach (RecordedAdmiralRequest r in requests.Where(r => r.Path != "/api/v1/status/health"))
                        AssertEqual("Bearer bearer-123", r.Authorization, r.Path);
                    AssertFalse(requests.Any(r => r.Path == "/api/v1/fleets" && r.Method == "POST"), "no default fleet is created on a remote Admiral");
                    AssertFalse(EmbeddedServer.IsRunning, "no embedded server");
                }
            }));

            cases.Add(CaseAsync("remote_401_and_403_are_clear_errors", "A throwaway Admiral: a bearer token works; no or a wrong token is a typed 401, a non-admin server stop a typed 403", TestTags.Negative, async () =>
            {
                SecurityTestServer server = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
                try
                {
                    await server.StartAsync().ConfigureAwait(false);
                    string bearer;
                    using (HttpClient admin = server.CreateRestClient(true))
                    {
                        HttpResponseMessage created = await admin.PostAsJsonAsync("/api/v1/credentials", new Credential("default", "default") { Name = "cli-test", Active = true }).ConfigureAwait(false);
                        AssertEqual(HttpStatusCode.Created, created.StatusCode);
                        Credential? credential = JsonSerializer.Deserialize<Credential>(await created.Content.ReadAsStringAsync().ConfigureAwait(false), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        bearer = credential?.BearerToken ?? "";
                        AssertFalse(String.IsNullOrEmpty(bearer), "bearer token");
                    }

                    AssertEqual(0, await RunCliAsync("fleet", "list", "--server", server.BaseUrl, "--token", bearer).ConfigureAwait(false));

                    AdmiralTargetException missing = await ExpectTargetErrorAsync(() => RunCliAsync("fleet", "list", "--server", server.BaseUrl)).ConfigureAwait(false);
                    AssertEqual(AdmiralTargetErrorEnum.Unauthorized, missing.ErrorCode);
                    AssertContains("none was sent", missing.Message);
                    AssertContains("--token", missing.Message);

                    AdmiralTargetException wrong = await ExpectTargetErrorAsync(() => RunCliAsync("fleet", "list", "--server", server.BaseUrl, "--token", "not-a-token")).ConfigureAwait(false);
                    AssertEqual(AdmiralTargetErrorEnum.Unauthorized, wrong.ErrorCode);
                    AssertContains("rejected the credential", wrong.Message);
                    AssertFalse(wrong.Message.Contains(bearer), "the message never contains a token");

                    // A valid credential of a user without the global admin role: server stop is a typed 403.
                    string userBearer;
                    using (HttpClient admin = server.CreateRestClient(true))
                    {
                        HttpResponseMessage userResponse = await admin.PostAsJsonAsync("/api/v1/users", new { TenantId = "default", Email = "cli-dev@example.com", Password = "Dev-User-Pass-2026", IsAdmin = false, IsTenantAdmin = false, Active = true }).ConfigureAwait(false);
                        UserMaster? user = JsonSerializer.Deserialize<UserMaster>(await userResponse.Content.ReadAsStringAsync().ConfigureAwait(false), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        AssertFalse(String.IsNullOrEmpty(user?.Id), "user created");
                        HttpResponseMessage created = await admin.PostAsJsonAsync("/api/v1/credentials", new Credential("default", user!.Id) { Name = "cli-dev", Active = true }).ConfigureAwait(false);
                        Credential? credential = JsonSerializer.Deserialize<Credential>(await created.Content.ReadAsStringAsync().ConfigureAwait(false), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        userBearer = credential?.BearerToken ?? "";
                    }

                    AdmiralTargetException forbidden = await ExpectTargetErrorAsync(() => RunCliAsync("server", "stop", "--server", server.BaseUrl, "--token", userBearer)).ConfigureAwait(false);
                    AssertEqual(AdmiralTargetErrorEnum.Forbidden, forbidden.ErrorCode);
                    AssertContains("HTTP 403", forbidden.Message);
                    AssertTrue(server.Server != null, "the Admiral is still running");

                    StringWriter error = new StringWriter();
                    AssertEqual(HelmErrorHandler.TargetErrorExitCode, HelmErrorHandler.Handle(wrong, error));
                    AssertContains("Error: HTTP 401", error.ToString());
                }
                finally
                {
                    server.Dispose();
                }
            }));

            cases.Add(CaseAsync("local_only_commands_refuse_remote_target", "server start, reset, config set, config init, and mcp stdio refuse a remote target with a typed error", TestTags.Negative, async () =>
            {
                string remote = "http://192.0.2.10:7890";
                Dictionary<string, string[]> commands = new Dictionary<string, string[]>
                {
                    ["server start"] = new[] { "server", "start", "--server", remote },
                    ["reset"] = new[] { "reset", "--force", "--server", remote },
                    ["config set"] = new[] { "config", "set", "admiralPort", "1", "--server", remote },
                    ["config init"] = new[] { "config", "init", "--server", remote },
                    ["mcp stdio"] = new[] { "mcp", "stdio", "--server", remote },
                };

                foreach (KeyValuePair<string, string[]> pair in commands)
                {
                    AdmiralTargetException ex = await ExpectTargetErrorAsync(() => RunCliAsync(pair.Value)).ConfigureAwait(false);
                    AssertEqual(AdmiralTargetErrorEnum.LocalOnlyCommand, ex.ErrorCode, pair.Key);
                    AssertEqual(pair.Key, ex.CommandName, pair.Key);
                    AssertContains("armada " + pair.Key, ex.Message, pair.Key);
                    AssertContains(remote, ex.Message, pair.Key);
                }

                AssertFalse(EmbeddedServer.IsRunning, "nothing started");
            }));

            cases.Add(CaseAsync("unreachable_remote_never_starts_embedded_server", "An unreachable remote target is a typed error and never starts the embedded local server", TestTags.Negative, async () =>
            {
                int port = TestPorts.Reserve(1)[0];
                AdmiralTargetException ex = await ExpectTargetErrorAsync(() => RunCliAsync("mission", "list", "--server", "http://127.0.0.1:" + port, "--token", "x")).ConfigureAwait(false);
                AssertEqual(AdmiralTargetErrorEnum.Unreachable, ex.ErrorCode);
                AssertContains("never starts a local server", ex.Message);
                AssertFalse(EmbeddedServer.IsRunning, "EnsureServerAsync must not start the embedded server for a remote target");
            }));

            cases.Add(CaseAsync("profile_commands_share_tui_store", "profile add/use/remove write the TUI's profile store and token store; the CLI then follows the active profile", TestTags.Positive, async () =>
            {
                using (RecordingAdmiralStub stub = new RecordingAdmiralStub())
                {
                    try
                    {
                        AssertEqual(0, await RunCliAsync("profile", "add", "stub-remote", "--server", stub.BaseUrl, "--token", "profile-token").ConfigureAwait(false));
                        PreferencesService prefs = new PreferencesService();
                        prefs.Load();
                        ServerProfile? saved = prefs.FindProfile("stub-remote");
                        AssertNotNull(saved, "profile saved in tui.json");
                        AssertEqual(false, saved!.FollowsLocalAdmiral);
                        AssertEqual("profile-token", await CredentialStoreFactory.Create().GetAsync(SessionService.CredentialKey(saved)).ConfigureAwait(false));
                        AssertFalse(File.ReadAllText(TuiPaths.PreferencesFile()).Contains("profile-token"), "tui.json never holds the token");

                        AssertEqual(0, await RunCliAsync("profile", "use", "stub-remote").ConfigureAwait(false));
                        AssertEqual(0, await RunCliAsync("mission", "list").ConfigureAwait(false));
                        AssertTrue(stub.Requests().Any(r => r.Path == "/api/v1/missions" && r.Authorization == "Bearer profile-token"), "the active profile and its stored token were used");

                        AdmiralTargetException reserved = await ExpectTargetErrorAsync(() => RunCliAsync("profile", "add", "local", "--server", stub.BaseUrl, "--no-token")).ConfigureAwait(false);
                        AssertEqual(AdmiralTargetErrorEnum.ReservedProfileName, reserved.ErrorCode);
                    }
                    finally
                    {
                        await RunCliAsync("profile", "remove", "stub-remote").ConfigureAwait(false);
                    }

                    PreferencesService after = new PreferencesService();
                    after.Load();
                    AssertNull(after.FindProfile("stub-remote"), "removed");
                    AssertNull(after.Current.ActiveProfile, "removing the active profile returns to the local Admiral");
                }
            }));

            cases.Add(CaseAsync("mcp_install_remote_configs_per_client", "mcp install for a remote Admiral writes the URL and an Authorization header per client, Codex via ARMADA_TOKEN, snippets redacted", TestTags.Positive, async () =>
            {
                string root = TestTemp.NewDirectory("mcp-remote");
                McpConfigHelper.McpInstallPaths paths = new McpConfigHelper.McpInstallPaths(Path.Combine(root, "home"), Path.Combine(root, "project"), Path.Combine(root, "home", ".mux"));
                string url = "https://armada.example.com:7891/mcp";
                string token = "secret-bearer-xyz";
                List<McpConfigHelper.ConfigTarget> targets = McpConfigHelper.BuildRemoteTargets(url, token, paths, true, true);
                AssertEqual(6, targets.Count, "Claude Code, Codex, Gemini, Cursor, Mux, OpenCode");

                foreach (McpConfigHelper.ConfigTarget target in targets)
                {
                    AssertFalse(McpConfigHelper.BuildManualSnippet(target).Contains(token), target.ClientName + " snippet hides the token");
                    if (target.Kind == McpClientKindEnum.Codex)
                    {
                        string args = String.Join(" ", target.InstallArgs ?? Array.Empty<string>());
                        AssertEqual("mcp add armada --url " + url + " --bearer-token-env-var ARMADA_TOKEN", args);
                        continue;
                    }

                    await McpConfigHelper.InstallTargetAsync(target).ConfigureAwait(false);
                    AssertTrue(target.FilePath.StartsWith(root, StringComparison.Ordinal), target.ClientName + " writes under the temp root");
                }

                JsonSerializerOptions options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                KeyedMcpServersDocument? claude = JsonSerializer.Deserialize<KeyedMcpServersDocument>(File.ReadAllText(Path.Combine(paths.HomeDirectory, ".claude.json")), options);
                KeyedMcpServerEntry claudeEntry = claude!.McpServers!["armada"];
                AssertEqual("http", claudeEntry.Type);
                AssertEqual(url, claudeEntry.Url);
                AssertEqual("Bearer " + token, claudeEntry.Headers!["Authorization"]);

                KeyedMcpServersDocument? gemini = JsonSerializer.Deserialize<KeyedMcpServersDocument>(File.ReadAllText(Path.Combine(paths.HomeDirectory, ".gemini", "settings.json")), options);
                AssertEqual(url, gemini!.McpServers!["armada"].HttpUrl);
                AssertEqual("Bearer " + token, gemini.McpServers["armada"].Headers!["Authorization"]);

                KeyedMcpServersDocument? cursor = JsonSerializer.Deserialize<KeyedMcpServersDocument>(File.ReadAllText(Path.Combine(paths.ProjectDirectory, ".cursor", "mcp.json")), options);
                AssertEqual(url, cursor!.McpServers!["armada"].Url);
                AssertEqual("Bearer " + token, cursor.McpServers["armada"].Headers!["Authorization"]);

                OpenCodeConfigFile? openCode = JsonSerializer.Deserialize<OpenCodeConfigFile>(File.ReadAllText(Path.Combine(paths.HomeDirectory, ".config", "opencode", "opencode.json")), options);
                AssertEqual("remote", openCode!.Mcp!["armada"].Type);
                AssertEqual("Bearer " + token, openCode.Mcp["armada"].Headers!["Authorization"]);

                MuxServersFile? mux = JsonSerializer.Deserialize<MuxServersFile>(File.ReadAllText(Path.Combine(paths.MuxConfigDirectory, "mcp-servers.json")), options);
                MuxServerEntry muxEntry = mux!.Servers!.Single(s => s.Name == "armada");
                AssertEqual("https://armada.example.com:7891", muxEntry.Url);
                AssertEqual("/mcp", muxEntry.McpPath);
                AssertEqual("Authorization", muxEntry.Auth!.ApiKeyHeader);
                AssertEqual("Bearer " + token, muxEntry.Auth.ApiKeyValue);
            }));

            cases.Add(Case("mcp_url_derivation", "mcp install derives the remote MCP URL from the target, the advertised port, or --mcp-url", TestTags.Positive, () =>
            {
                AdmiralTarget target = new AdmiralTarget("https://armada.example.com", AdmiralTargetSourceEnum.Flag, null, false, "t", null, AdmiralCredentialSourceEnum.Flag);
                AssertEqual("https://armada.example.com:7891/mcp", McpInstallCommand.ResolveRemoteMcpUrl(target, null));
                AssertEqual("https://armada.example.com:23211/mcp", McpInstallCommand.ResolveRemoteMcpUrl(target, null, 23211));
                AssertEqual("https://mcp.example.com/mcp", McpInstallCommand.ResolveRemoteMcpUrl(target, "https://mcp.example.com"));
                AssertEqual("https://mcp.example.com/armada/mcp", McpInstallCommand.ResolveRemoteMcpUrl(target, "https://mcp.example.com/armada/mcp"));
            }));

            cases.Add(CaseAsync("embedded_server_reads_camelcase_settings", "The embedded server honors camelCase settings.json keys (it used to bind the default ports and overwrite them)", TestTags.Negative, async () =>
            {
                string dir = TestTemp.NewDirectory("embedded-settings");
                string path = Path.Combine(dir, "settings.json");
                File.WriteAllText(path, "{\n  \"admiralPort\": 23290,\n  \"mcpPort\": 23291,\n  \"rest\": { \"hostname\": \"127.0.0.1\" }\n}\n");
                Armada.Core.Settings.ArmadaSettings loaded = await EmbeddedServer.LoadSettingsAsync(path).ConfigureAwait(false);
                AssertEqual(23290, loaded.AdmiralPort, "admiralPort");
                AssertEqual(23291, loaded.McpPort, "mcpPort");
                AssertEqual("127.0.0.1", loaded.Rest.Hostname, "rest.hostname");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Helm Remote Target",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<int> RunCliAsync(params string[] args)
        {
            // Host independence: a developer's exported ARMADA_SERVER_URL, ARMADA_URL, or ARMADA_TOKEN must not change
            // what these runs target. The runner executes cases one at a time, so the process environment is safe to
            // clear for the duration of the call.
            string[] names = new[] { AdmiralTargetResolver.ServerUrlEnvironmentVariable, AdmiralTargetResolver.LegacyServerUrlEnvironmentVariable, AdmiralTargetResolver.TokenEnvironmentVariable };
            Dictionary<string, string?> saved = new Dictionary<string, string?>();
            foreach (string name in names)
            {
                saved[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, null);
            }

            try
            {
                CommandApp app = new CommandApp(new TypeRegistrar());
                app.Configure(config =>
                {
                    Program.ConfigureCommands(config);
                    config.PropagateExceptions();
                });
                return await app.RunAsync(args).ConfigureAwait(false);
            }
            finally
            {
                foreach (KeyValuePair<string, string?> pair in saved) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }

        private static async Task<AdmiralTargetException> ExpectTargetErrorAsync<T>(Func<Task<T>> action)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (AdmiralTargetException ex)
            {
                return ex;
            }

            throw new AssertionException("expected an AdmiralTargetException");
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
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
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion

        #region Private-Classes

        private sealed class ProfileFixture
        {
            public PreferencesService Preferences { get; }

            public ICredentialStore Credentials { get; }

            private ProfileFixture(PreferencesService prefs, ICredentialStore credentials)
            {
                Preferences = prefs;
                Credentials = credentials;
            }

            public static async Task<ProfileFixture> CreateAsync(string? name, string? url, string? token, bool active)
            {
                string dir = TestTemp.NewDirectory("target-prefs");
                PreferencesService prefs = new PreferencesService(Path.Combine(dir, "tui.json"));
                prefs.Load();
                FileCredentialStore store = new FileCredentialStore(Path.Combine(dir, "tui-credentials.json"));
                if (name != null && url != null)
                {
                    ServerProfile profile = prefs.UpsertProfile(name, url);
                    if (!String.Equals(name, "default", StringComparison.Ordinal)) profile.FollowsLocalAdmiral = false;
                    if (token != null) await store.SetAsync(SessionService.CredentialKey(profile), token).ConfigureAwait(false);
                    prefs.Current.ActiveProfile = active ? profile.Name : null;
                }

                return new ProfileFixture(prefs, store);
            }

            public AdmiralTargetResolver Resolver(Dictionary<string, string?> env)
            {
                return new AdmiralTargetResolver(k => env.TryGetValue(k, out string? v) ? v : null, Preferences, Credentials, LocalPort, LocalKey);
            }
        }

        #endregion
    }
}
