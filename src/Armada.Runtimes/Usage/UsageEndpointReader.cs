namespace Armada.Runtimes.Usage
{
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;
    /// <summary>Bounded quota-only HTTP reads; no credential refresh, redirects, retries or model calls.</summary>
    internal static class UsageEndpointReader
    {
        internal static async Task<string?> ReadAsync(HttpClient client, Uri endpoint, string accessToken, string? body, UsageProbeResult result, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 16384 || System.Linq.Enumerable.Any(accessToken, value => Char.IsControl(value) || Char.IsWhiteSpace(value)))
            { result.Status = Armada.Core.Enums.UsageSnapshotStatusEnum.AuthMissing; result.ErrorCode = "CredentialUnavailable"; return null; }
            using (HttpRequestMessage request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, endpoint))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                if (endpoint.Host == "api.anthropic.com") request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
                if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    { result.Status = Armada.Core.Enums.UsageSnapshotStatusEnum.AuthMissing; result.ErrorCode = "CredentialUnavailable"; return null; }
                    if ((int)response.StatusCode == 429)
                    {
                        result.ErrorCode = "ProviderThrottled";
                        RetryConditionHeaderValue? retry = response.Headers.RetryAfter;
                        result.RetryAfterUtc = retry?.Date?.UtcDateTime ?? (retry?.Delta.HasValue == true ? DateTime.UtcNow.Add(retry.Delta.Value) : null);
                        return null;
                    }
                    if (!response.IsSuccessStatusCode) { result.ErrorCode = "UsageEndpointUnavailable"; return null; }
                    if (response.Content.Headers.ContentLength > 65536) { result.ErrorCode = "OversizeOutput"; return null; }
                    using (Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                    using (MemoryStream bytes = new MemoryStream())
                    {
                        byte[] buffer = new byte[4096];
                        while (true)
                        {
                            int read = await stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                            if (read == 0) break;
                            if (bytes.Length + read > 65536) { result.ErrorCode = "OversizeOutput"; return null; }
                            bytes.Write(buffer, 0, read);
                        }
                        return Encoding.UTF8.GetString(bytes.ToArray());
                    }
                }
            }
        }
    }
}
