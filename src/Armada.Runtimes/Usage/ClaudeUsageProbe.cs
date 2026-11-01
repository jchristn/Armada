namespace Armada.Runtimes.Usage
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Services;
    /// <summary>Read-only Claude account quota on the credential host. No model request or credential refresh.</summary>
    public class ClaudeUsageProbe : IUsageProbe
    {
        /// <summary>Compatibility code for an unavailable active source.</summary>
        public const string UnverifiedCode = "ClaudeQuotaUnavailable";
        private readonly UsageAccountResolver? _Accounts;
        private readonly HttpClient _Client;
        private readonly Func<CancellationToken, Task<UsageProbeCredential>>? _Credential;
        /// <summary>Create a host-local probe. An injected client supports deterministic transport tests.</summary>
        public ClaudeUsageProbe(UsageAccountResolver? accounts = null, HttpClient? client = null, Func<CancellationToken, Task<UsageProbeCredential>>? credential = null)
        {
            _Accounts = accounts;
            _Credential = credential;
            _Client = client ?? _DefaultClient;
        }
        private static readonly HttpClient _DefaultClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
        /// <inheritdoc />
        public async Task<UsageProbeResult> CaptureAsync(UsageProbeHost host, CancellationToken token = default)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (_Credential == null && (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")) || !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN")) || Environment.GetEnvironmentVariable("CLAUDE_CODE_USE_BEDROCK") == "1" || Environment.GetEnvironmentVariable("CLAUDE_CODE_USE_VERTEX") == "1"))
                return UsageProbeResult.Unsupported("ApiKeyMode", "subscription limits are unavailable for the configured API provider mode");
            if (String.IsNullOrEmpty(host.HostId) || String.IsNullOrEmpty(host.TenantId)) return UsageProbeResult.Failed("InvalidProbeContext");
            if (_Credential == null)
            {
                UsageCliSettings? selection = await UsageCredentialReader.ReadFileAsync<UsageCliSettings>(Path.Combine(UsageCredentialReader.ClaudeDirectory, "settings.json"), token).ConfigureAwait(false);
                if (!String.IsNullOrWhiteSpace(selection?.ApiKeyHelper))
                    return UsageProbeResult.Unsupported("ApiKeyMode", "the CLI authentication selection does not use the subscription account; no OAuth quota read is made");
            }
            if (_Accounts == null) return UsageProbeResult.Failed("IdentityUnavailable");
            UsageProbeCredential credential = _Credential == null ? await ReadCredentialAsync(token).ConfigureAwait(false) : await _Credential(token).ConfigureAwait(false);
            string? accessToken = credential.AccessToken;
            string? identity = credential.AccountIdentity;
            if (String.IsNullOrWhiteSpace(accessToken)) return UsageProbeResult.AuthMissing("CredentialUnavailable", "sign in to Claude on the named credential host");
            string? account = _Accounts?.DeriveAccountKey(host.TenantId, host.Runtime, identity, credential.Profile);
            if (account == null) return UsageProbeResult.Failed("IdentityUnavailable");
            UsageProbeResult result = new UsageProbeResult { AccountKey = account, Provider = "anthropic", Source = UsageCaptureSourceEnum.OAuthEndpoint };
            try
            {
                string? json = await UsageEndpointReader.ReadAsync(_Client, new Uri("https://api.anthropic.com/api/oauth/usage"), accessToken, null, result, token).ConfigureAwait(false);
                if (json != null) result = UsageQuotaParsers.Claude(json, DateTime.UtcNow);
                result.AccountKey = account;
                result.Provider = "anthropic";
                if (result.Status == UsageSnapshotStatusEnum.Ok || result.RetryAfterUtc.HasValue || result.ErrorCode == "ProviderThrottled" || !host.AllowPty) return result;
                UsageProbeResult fallback = await new InteractiveUsageReader().CaptureAsync(host, account, token).ConfigureAwait(false);
                return fallback.Status == UsageSnapshotStatusEnum.Ok ? fallback : result;
            }
            catch (HttpRequestException)
            {
                return host.AllowPty ? await new InteractiveUsageReader().CaptureAsync(host, account, token).ConfigureAwait(false) : UsageProbeResult.Failed("UsageEndpointUnavailable");
            }
        }
        private static async Task<UsageProbeCredential> ReadCredentialAsync(CancellationToken token)
        {
            UsageOAuthCredentials? credentials = await UsageCredentialReader.ClaudeAsync(token).ConfigureAwait(false);
            string? accessToken = credentials?.AccessToken;
            UsageOAuthCredentials? profile = await UsageCredentialReader.ReadAsync(Path.Combine(UsageCredentialReader.ClaudeDirectory, ".claude.json"), token, 8 * 1024 * 1024).ConfigureAwait(false);
            if (profile == null && String.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"))) profile = await UsageCredentialReader.ReadAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json"), token, 8 * 1024 * 1024).ConfigureAwait(false);
            string? identity = String.IsNullOrWhiteSpace(profile?.OauthAccount?.AccountUuid) ? null : profile.OauthAccount.AccountUuid + "|organization=" + (profile.OauthAccount.OrganizationUuid ?? String.Empty);
            return new UsageProbeCredential { AccessToken = accessToken, AccountIdentity = identity, Profile = UsageCredentialReader.ClaudeDirectory };
        }
    }
}
