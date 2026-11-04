namespace Armada.Runtimes.Usage
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    /// <summary>Typed, bounded parsers. Partial windows preserve unknown values instead of inventing zeros.</summary>
    public static class UsageQuotaParsers
    {
        /// <summary>Parse the Claude OAuth usage v1 shape.</summary>
        public static UsageProbeResult Claude(string json, DateTime observed)
        {
            UsageProbeResult result = Empty(observed);
            try
            {
                if (json.Length > 65536) return Failure("OversizeOutput");
                ClaudeUsageResponse? response = JsonSerializer.Deserialize<ClaudeUsageResponse>(json, UsageCredentialReader.Options);
                if (response == null) return Failure("MalformedOutput");
                Dictionary<string, ClaudeUsageWindowDto?> windows = new Dictionary<string, ClaudeUsageWindowDto?> { ["five_hour"] = response.FiveHour, ["seven_day"] = response.SevenDay, ["seven_day_opus"] = response.SevenDayOpus, ["seven_day_sonnet"] = response.SevenDaySonnet };
                foreach (KeyValuePair<string, ClaudeUsageWindowDto?> pair in windows)
                {
                    if (!Regex.IsMatch(pair.Key, "^(five_hour|seven_day)(_[a-z0-9_]{1,64})?$")) continue;
                    if (pair.Value == null) continue;
                    double? percent = pair.Value.Utilization;
                    if (percent.HasValue && (!Double.IsFinite(percent.Value) || percent < 0 || percent > 100)) return Failure("MalformedOutput");
                    result.Windows.Add(new UsageWindow { Name = pair.Key, Model = pair.Key == "seven_day_opus" ? "opus" : pair.Key == "seven_day_sonnet" ? "sonnet" : null, UsedPercent = percent, ResetsUtc = Reset(pair.Value.ResetsAt, observed), Unit = "percent" });
                    result.Windows[result.Windows.Count - 1].ResetKind = FixedWhenReset(result.Windows[result.Windows.Count - 1]);
                }
            }
            catch (JsonException) { return Failure("MalformedOutput"); }
            return Finish(result);
        }
        /// <summary>Parse the Gemini Code Assist v1internal response, preserving model and unit scope.</summary>
        public static UsageProbeResult Gemini(string json, DateTime observed)
        {
            UsageProbeResult result = Empty(observed);
            try
            {
                if (json.Length > 65536) return Failure("OversizeOutput");
                GeminiQuotaResponse? response = JsonSerializer.Deserialize<GeminiQuotaResponse>(json, UsageCredentialReader.Options);
                if (response?.Buckets == null || response.Buckets.Count > 32) return Failure("MalformedOutput");
                foreach (GeminiQuotaBucket bucket in response.Buckets)
                {
                    if (bucket == null || String.IsNullOrWhiteSpace(bucket.ModelId) || bucket.ModelId.Length > 128 || !Regex.IsMatch(bucket.ModelId, "^[a-zA-Z0-9][a-zA-Z0-9._:/-]*$") || (bucket.TokenType != null && !Regex.IsMatch(bucket.TokenType, "^[A-Za-z0-9_]{1,64}$"))) return Failure("MalformedOutput");
                    double? remaining = bucket.RemainingFraction;
                    if (remaining.HasValue && (!Double.IsFinite(remaining.Value) || remaining < 0 || remaining > 1)) return Failure("MalformedOutput");
                    result.Windows.Add(new UsageWindow { Name = "quota_" + (bucket.TokenType ?? "unknown"), Model = bucket.ModelId, UsedPercent = remaining.HasValue ? (1 - remaining.Value) * 100 : null, ResetsUtc = Reset(bucket.ResetTime, observed), Unit = "percent" });
                    result.Windows[result.Windows.Count - 1].ResetKind = FixedWhenReset(result.Windows[result.Windows.Count - 1]);
                }
            }
            catch (JsonException) { return Failure("MalformedOutput"); }
            return Finish(result);
        }
        // Claude five_hour/seven_day and Gemini daily quota windows reset entirely at the reported absolute time.
        private static UsageWindowResetKindEnum FixedWhenReset(UsageWindow window)
            => window.ResetsUtc.HasValue ? UsageWindowResetKindEnum.Fixed : UsageWindowResetKindEnum.Unknown;

        private static UsageProbeResult Failure(string code)
        {
            UsageProbeResult result = UsageProbeResult.Failed(code);
            result.Source = UsageCaptureSourceEnum.OAuthEndpoint;
            return result;
        }
        private static UsageProbeResult Empty(DateTime observed) => new UsageProbeResult { Source = UsageCaptureSourceEnum.OAuthEndpoint, CapturedUtc = observed };
        private static UsageProbeResult Finish(UsageProbeResult result)
        {
            if (result.Windows.Count == 0) { result.Status = UsageSnapshotStatusEnum.Unsupported; result.ErrorCode = "NoQuotaWindows"; return result; }
            result.Status = UsageSnapshotStatusEnum.Ok;
            return result;
        }
        private static DateTime? Reset(string? text, DateTime observed)
        {
            if (String.IsNullOrEmpty(text)) return null;
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset value) || !text.Contains('T') || !(text.EndsWith('Z') || Regex.IsMatch(text, "[+-][0-9]{2}:[0-9]{2}$"))) throw new JsonException();
            if (value.UtcDateTime < observed.AddMinutes(-5) || value.UtcDateTime > observed.AddDays(8)) throw new JsonException();
            return value.UtcDateTime;
        }
    }
}
