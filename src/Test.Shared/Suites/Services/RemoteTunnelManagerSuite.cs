namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Server;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the <see cref="RemoteTunnelManager"/>: tunnel URL normalization for http/https
    /// and shorthand base URLs, rejection of invalid inputs, capability-manifest construction against
    /// the current release version, disabled default status when the feature is off, and reconnect
    /// delay computation honoring configured bounds.
    /// </summary>
    public sealed class RemoteTunnelManagerSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.RemoteTunnelManager";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Remote Tunnel Manager suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("try_normalize_tunnel_url_converts_http_and_https_schemes", "TryNormalizeTunnelUrl Converts HttpAndHttps Schemes", TestTags.Positive, () =>
            {
                AssertTrue(RemoteTunnelManager.TryNormalizeTunnelUrl("https://control.example.com/tunnel?x=1", out Uri? wssUri, out string? httpsError), httpsError ?? "HTTPS tunnel URL should normalize");
                AssertNotNull(wssUri);
                AssertEqual("wss", wssUri!.Scheme);
                AssertEqual("/tunnel", wssUri.AbsolutePath);
                AssertEqual("?x=1", wssUri.Query);

                AssertTrue(RemoteTunnelManager.TryNormalizeTunnelUrl("http://control.example.com/tunnel", out Uri? wsUri, out string? httpError), httpError ?? "HTTP tunnel URL should normalize");
                AssertNotNull(wsUri);
                AssertEqual("ws", wsUri!.Scheme);

                AssertTrue(RemoteTunnelManager.TryNormalizeTunnelUrl("http://control.example.com:7893", out Uri? shorthandUri, out string? shorthandError), shorthandError ?? "Base proxy URL should normalize");
                AssertNotNull(shorthandUri);
                AssertEqual("ws", shorthandUri!.Scheme);
                AssertEqual("/tunnel", shorthandUri.AbsolutePath);

                AssertTrue(RemoteTunnelManager.TryNormalizeTunnelUrl("wss://control.example.com", out Uri? shorthandSecureUri, out string? shorthandSecureError), shorthandSecureError ?? "Base websocket URL should normalize");
                AssertNotNull(shorthandSecureUri);
                AssertEqual("wss", shorthandSecureUri!.Scheme);
                AssertEqual("/tunnel", shorthandSecureUri.AbsolutePath);
            }));

            cases.Add(Case("try_normalize_tunnel_url_rejects_invalid_inputs", "TryNormalizeTunnelUrl Rejects Invalid Inputs", TestTags.Negative, () =>
            {
                AssertFalse(RemoteTunnelManager.TryNormalizeTunnelUrl(null, out Uri? _, out string? missingError, out string? missingCode));
                AssertEqual(RemoteTunnelErrorCodes.MissingTunnelUrl, missingCode, "Missing URL has a typed code");
                AssertFalse(String.IsNullOrWhiteSpace(missingError), "Missing URL keeps a human-readable message");

                AssertFalse(RemoteTunnelManager.TryNormalizeTunnelUrl("ftp://example.com/tunnel", out Uri? _, out string? schemeError, out string? schemeCode));
                AssertEqual(RemoteTunnelErrorCodes.UnsupportedScheme, schemeCode, "Unsupported scheme has a typed code");
                AssertFalse(String.IsNullOrWhiteSpace(schemeError), "Unsupported scheme keeps a human-readable message");

                AssertFalse(RemoteTunnelManager.TryNormalizeTunnelUrl("not a uri", out Uri? _, out string? _, out string? relativeCode));
                AssertEqual(RemoteTunnelErrorCodes.InvalidTunnelUrl, relativeCode, "A relative or malformed URL has a typed code");
            }));

            cases.Add(Case("build_capability_manifest_uses_current_release_version", "BuildCapabilityManifest UsesCurrentReleaseVersion", TestTags.Positive, () =>
            {
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                ArmadaSettings settings = new ArmadaSettings();
                RemoteTunnelManager manager = new RemoteTunnelManager(logging, settings);

                RemoteTunnelCapabilityManifest manifest = manager.BuildCapabilityManifest();

                AssertEqual(Constants.RemoteTunnelProtocolVersion, manifest.ProtocolVersion);
                AssertEqual(Constants.ProductVersion, manifest.ArmadaVersion);
                AssertContains("remoteControl.handshake", String.Join(",", manifest.Features), "Handshake capability should be advertised");
                AssertContains("remoteControl.requests", String.Join(",", manifest.Features), "Request capability should be advertised");
                AssertContains("dashboard.http.relay", String.Join(",", manifest.Features), "Dashboard HTTP relay capability should be advertised");
                AssertContains("dashboard.websocket.relay", String.Join(",", manifest.Features), "Dashboard websocket relay capability should be advertised");
                AssertEqual(6, manifest.Features.Count, "Relay-only capability manifest should stay intentionally small");
                AssertFalse(manifest.Features.Contains("status.health"), "Legacy feature-specific capabilities should no longer be advertised");
                AssertFalse(manifest.Features.Contains("objective.create"), "Legacy objective tunnel methods should no longer be advertised");
            }));

            cases.Add(Case("get_status_defaults_to_disabled_when_feature_disabled", "GetStatus DefaultsToDisabledWhenFeatureDisabled", TestTags.Positive, () =>
            {
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                ArmadaSettings settings = new ArmadaSettings();
                settings.RemoteControl.Enabled = false;

                RemoteTunnelManager manager = new RemoteTunnelManager(logging, settings);
                RemoteTunnelStatus status = manager.GetStatus();

                AssertFalse(status.Enabled);
                AssertEqual(RemoteTunnelStateEnum.Disabled, status.State);
                AssertNotNull(status.CapabilityManifest);
                AssertTrue(status.InstanceId.StartsWith("armada-"), "Auto-generated instance ID should be stable and prefixed");
            }));

            cases.Add(Case("status_reports_typed_error_code", "A misconfigured tunnel reports LastErrorCode in the status", TestTags.Negative, () =>
            {
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                ArmadaSettings settings = new ArmadaSettings();
                settings.RemoteControl.Enabled = true;
                settings.RemoteControl.TunnelUrl = "ftp://example.invalid/tunnel";

                RemoteTunnelManager manager = new RemoteTunnelManager(logging, settings);
                using (CancellationTokenSource cts = new CancellationTokenSource())
                {
                    manager.Start(cts.Token);
                    try
                    {
                        bool reported = SpinWait.SpinUntil(() => manager.GetStatus().State == RemoteTunnelStateEnum.Error, TimeSpan.FromSeconds(30));
                        AssertTrue(reported, "the manager reports the configuration error");
                        RemoteTunnelStatus status = manager.GetStatus();
                        AssertEqual(RemoteTunnelErrorCodes.UnsupportedScheme, status.LastErrorCode, "typed code for the unsupported scheme");
                        AssertFalse(String.IsNullOrWhiteSpace(status.LastError), "human-readable message kept");
                    }
                    finally
                    {
                        cts.Cancel();
                        manager.StopAsync().GetAwaiter().GetResult();
                    }
                }
            }));

            cases.Add(Case("compute_reconnect_delay_honors_configured_bounds", "ComputeReconnectDelay HonorsConfiguredBounds", TestTags.Positive, () =>
            {
                RemoteControlSettings settings = new RemoteControlSettings
                {
                    ReconnectBaseDelaySeconds = 4,
                    ReconnectMaxDelaySeconds = 10
                };

                TimeSpan delay = RemoteTunnelManager.ComputeReconnectDelay(settings, 8);
                AssertTrue(delay.TotalSeconds >= 9.0, "Jittered delay should stay near the capped maximum");
                AssertTrue(delay.TotalSeconds <= 11.0, "Jittered delay should stay within the capped maximum band");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Remote Tunnel Manager",
                cases: cases);
        }

        #endregion

        #region Private-Methods

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

        #endregion
    }
}
