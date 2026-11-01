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
    /// <summary>Read-only Gemini account quota on the credential host. No model request or credential refresh.</summary>
    public class GeminiUsageProbe : IUsageProbe
    {
        /// <summary>Compatibility code for an unavailable active source.</summary>
        public const string UnavailableCode = "GeminiQuotaUnavailable";
        private readonly UsageAccountResolver? _Accounts;
        private readonly HttpClient _Client;
        private readonly Func<CancellationToken, Task<UsageProbeCredential>>? _Credential;
        /// <summary>Create a host-local probe. An injected client supports deterministic transport tests.</summary>
        public GeminiUsageProbe(UsageAccountResolver? accounts = null, HttpClient? client = null, Func<CancellationToken, Task<UsageProbeCredential>>? credential = null)
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
            if (_Credential == null && (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("GEMINI_API_KEY")) || !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("GOOGLE_API_KEY")) || Environment.GetEnvironmentVariable("GOOGLE_GENAI_USE_VERTEXAI") == "true"))
                return UsageProbeResult.Unsupported("ApiKeyMode", "account quota capture requires the existing Google-login profile; no configured estimate budget is available for API-key mode");
            if (String.IsNullOrEmpty(host.HostId) || String.IsNullOrEmpty(host.TenantId)) return UsageProbeResult.Failed("InvalidProbeContext");
            if (_Credential == null)
            {
                UsageCliSettings? selection = await UsageCredentialReader.ReadFileAsync<UsageCliSettings>(Path.Combine(UsageCredentialReader.GeminiDirectory, "settings.json"), token).ConfigureAwait(false);
                if (!String.IsNullOrWhiteSpace(selection?.Security?.Auth?.SelectedType) && !String.Equals(selection.Security.Auth.SelectedType, "oauth-personal", StringComparison.OrdinalIgnoreCase))
                    return UsageProbeResult.Unsupported("ApiKeyMode", "the CLI authentication selection does not use the subscription account; no OAuth quota read is made");
            }
            if (_Accounts == null) return UsageProbeResult.Failed("IdentityUnavailable");
            UsageProbeCredential credential = _Credential == null ? await ReadCredentialAsync(token).ConfigureAwait(false) : await _Credential(token).ConfigureAwait(false);
            string? accessToken = credential.AccessToken;
            string? identity = credential.AccountIdentity;
            if (String.IsNullOrWhiteSpace(accessToken)) return UsageProbeResult.AuthMissing("CredentialUnavailable", "sign in to Gemini on the named credential host");
            if (String.IsNullOrWhiteSpace(identity)) return UsageProbeResult.Failed("IdentityUnavailable");
            if (_Credential == null)
            {
                // The token store may fall back to a file. Verify it still belongs to the CLI's active
                // profile before using it; stale credentials for another login must never be borrowed.
                UsageProbeResult identityRead = new UsageProbeResult { Source = UsageCaptureSourceEnum.OAuthEndpoint, Provider = "google" };
                string? userInfo = await UsageEndpointReader.ReadAsync(_Client, new Uri("https://www.googleapis.com/oauth2/v2/userinfo"), accessToken, null, identityRead, token).ConfigureAwait(false);
                if (userInfo == null) return identityRead;
                GeminiAccountInfo? providerIdentity;
                try { providerIdentity = JsonSerializer.Deserialize<GeminiAccountInfo>(userInfo, UsageCredentialReader.Options); }
                catch (JsonException) { return UsageProbeResult.Failed("IdentityUnavailable"); }
                if (String.IsNullOrWhiteSpace(providerIdentity?.Id) || String.IsNullOrWhiteSpace(providerIdentity.Email)) return UsageProbeResult.Failed("IdentityUnavailable");
                if (!String.Equals(providerIdentity.Email, identity, StringComparison.OrdinalIgnoreCase))
                    return UsageProbeResult.Failed("AccountMismatch", "the credential does not match the active CLI profile on this host");
                identity = providerIdentity.Id;
            }
            string? account = _Accounts?.DeriveAccountKey(host.TenantId, host.Runtime, identity, credential.Profile);
            if (account == null) return UsageProbeResult.Failed("IdentityUnavailable");
            UsageProbeResult result = new UsageProbeResult { AccountKey = account, Provider = "google", Source = UsageCaptureSourceEnum.OAuthEndpoint };
            try
            {
                string? project = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT") ?? Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT_ID");
                if (String.IsNullOrWhiteSpace(project))
                {
                    string? loaded = await UsageEndpointReader.ReadAsync(_Client, new Uri("https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist"), accessToken, "{\"metadata\":{\"ideType\":\"IDE_UNSPECIFIED\",\"platform\":\"PLATFORM_UNSPECIFIED\",\"pluginType\":\"GEMINI\"}}", result, token).ConfigureAwait(false);
                    if (loaded != null) project = JsonSerializer.Deserialize<GeminiProjectResponse>(loaded, UsageCredentialReader.Options)?.CloudaicompanionProject;
                    if (String.IsNullOrWhiteSpace(project))
                    {
                        result.AccountKey = null;
                        if (result.ErrorCode == null) result.ErrorCode = "GeminiProjectUnavailable";
                        return result;
                    }
                }
                // Project is part of the active credential profile: independent project budgets cannot
                // overwrite one account series merely because the same Google login can access both.
                string? scopedAccount = _Accounts?.DeriveAccountKey(host.TenantId, host.Runtime, identity, credential.Profile + "|project=" + project);
                if (scopedAccount == null) return UsageProbeResult.Failed("IdentityUnavailable");
                account = scopedAccount;
                result.AccountKey = account;
                string? json = await UsageEndpointReader.ReadAsync(_Client, new Uri("https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota"), accessToken, JsonSerializer.Serialize(new Dictionary<string, string?> { ["project"] = project }), result, token).ConfigureAwait(false);
                if (json != null) result = UsageQuotaParsers.Gemini(json, DateTime.UtcNow);
                result.AccountKey = account;
                result.Provider = "google";
                if (result.Status == UsageSnapshotStatusEnum.Ok || result.RetryAfterUtc.HasValue || result.ErrorCode == "ProviderThrottled" || !host.AllowPty) return result;
                UsageProbeResult fallback = await new InteractiveUsageReader().CaptureAsync(host, account, token).ConfigureAwait(false);
                return fallback.Status == UsageSnapshotStatusEnum.Ok ? fallback : result;
            }
            catch (JsonException) { return UsageProbeResult.Failed("MalformedOutput"); }
            catch (HttpRequestException)
            {
                return host.AllowPty ? await new InteractiveUsageReader().CaptureAsync(host, account, token).ConfigureAwait(false) : UsageProbeResult.Failed("UsageEndpointUnavailable");
            }
        }
        private static async Task<UsageProbeCredential> ReadCredentialAsync(CancellationToken token)
        {
            UsageOAuthCredentials? credentials = await GeminiCredentialStore.ReadAsync(token).ConfigureAwait(false);
            string? accessToken = credentials?.GoogleAccessToken;
            UsageOAuthCredentials? profile = await UsageCredentialReader.ReadAsync(Path.Combine(UsageCredentialReader.GeminiDirectory, "google_accounts.json"), token).ConfigureAwait(false);
            string? identity = profile?.Active;
            return new UsageProbeCredential { AccessToken = accessToken, AccountIdentity = identity, Profile = UsageCredentialReader.GeminiDirectory };
        }
    }
}
