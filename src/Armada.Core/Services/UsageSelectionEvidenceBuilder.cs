namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    // Shared normalization for previews and transaction-time admission checks.
    internal static class UsageSelectionEvidenceBuilder
    {
        internal static void Apply(UsageSelectionCandidate candidate, Captain captain, UsageSelectionAccountEvidence account,
            IReadOnlyList<UsageSelectionBudgetTotals> totals, string tenantId, bool captureEnabled)
        {
            string? fingerprint = captain.UsageCaptureMode == UsageCaptureModeEnum.Custom ? UsageAccountResolver.CommandFingerprint(captain.UsageCaptureCommand) : null;
            string revision = UsageAccountResolver.ConfigurationRevision(account.HostId, captain.Runtime, fingerprint);
            string groupKey = UsageAccountResolver.GroupKey(tenantId, account.HostId, captain.Runtime, fingerprint);
            candidate.CooldownUntilUtc = account.CooldownUntilUtc;
            candidate.Provider = account.Provider;
            candidate.IdentityVerified = account.Runtime == captain.Runtime && captain.UsageCaptureActive && captureEnabled;
            candidate.ConfigurationMatches = account.ConfigurationRevision == revision && account.CaptureContextKey == UsageAccountResolver.CaptureContextKey(groupKey, revision)
                && (String.IsNullOrEmpty(captain.UsageCaptureHarborId) || captain.UsageCaptureHarborId == account.HostId);
            candidate.CapturedUtc = account.CapturedUtc; candidate.Status = account.Status; candidate.Source = account.Source;
            candidate.EffectiveCaptureIntervalSeconds = account.EffectiveIntervalSeconds;
            List<UsageSelectionBudgetTotals> accountTotals = totals.Where(t => t.HostId == account.HostId && t.Runtime == account.Runtime.ToString() && t.AccountKey == account.AccountKey)
                .DistinctBy(t => t.WindowKey + ":" + t.Generation).ToList();
            candidate.HasUncertainDebt = accountTotals.Any(t => t.HasUncertainDebt);
            foreach (UsageWindow window in UsageWindowsJson.Deserialize(account.WindowsJson))
            {
                if (window == null) { candidate.Windows.Add(new UsageSelectionWindowEvidence()); continue; }
                string key = UsageBudgetIdentity.WindowKey(window, captain.UsageCaptureMode == UsageCaptureModeEnum.Custom ? account.ConfigurationRevision : null);
                candidate.Windows.Add(new UsageSelectionWindowEvidence { Window = window,
                    ScopeApplies = String.IsNullOrEmpty(window.Scope) || String.Equals(window.Scope, "account", StringComparison.OrdinalIgnoreCase),
                    GenerationConfirmed = !String.IsNullOrWhiteSpace(window.Name)
                        && UsageSelectionEvidenceSanitizer.Label(window.Name) == window.Name
                        && UsageSelectionEvidenceSanitizer.Label(window.Model) == window.Model
                        && UsageSelectionEvidenceSanitizer.Label(window.Scope) == window.Scope
                        && UsageSelectionEvidenceSanitizer.Label(window.Unit) == window.Unit,
                    ActiveReservationPercent = accountTotals.Where(t => t.WindowKey == key).Sum(t => t.ActivePercent),
                    UnsettledPercent = accountTotals.Where(t => t.WindowKey == key).Sum(t => t.UnsettledPercent) });
            }
            foreach (IGrouping<string, UsageSelectionWindowEvidence> duplicate in candidate.Windows.GroupBy(w => UsageBudgetIdentity.WindowKey(w.Window)).Where(g => g.Count() > 1))
                foreach (UsageSelectionWindowEvidence window in duplicate) window.GenerationConfirmed = false;
        }

        internal static bool CanReserve(UsageSelectionCandidate input, UsageSelectionWindowEvidence evidence, UsageSelectionSettings settings, DateTime nowUtc)
        {
            UsageWindow window = evidence.Window;
            bool known = window.UsedPercent.HasValue ? Double.IsFinite(window.UsedPercent.Value) && window.UsedPercent >= 0
                : window.Used >= 0 && window.Limit > 0 && !String.IsNullOrWhiteSpace(window.Unit);
            bool applicable = evidence.ScopeApplies && evidence.GenerationConfirmed
                && (String.IsNullOrWhiteSpace(window.Model) || String.Equals(window.Model.Trim(), input.Model?.Trim(), StringComparison.OrdinalIgnoreCase));
            return input.IdentityVerified && input.ConfigurationMatches && input.Status == UsageSnapshotStatusEnum.Ok
                && input.Source != UsageCaptureSourceEnum.Derived && Enum.IsDefined(typeof(UsageCaptureSourceEnum), input.Source)
                && applicable && known && input.CapturedUtc.HasValue && input.CapturedUtc <= nowUtc + UsageQuotaPolicy.MaximumFutureSkew
                && (nowUtc - input.CapturedUtc.Value).TotalSeconds <= Math.Min(settings.MaximumFreshnessSeconds,
                    UsageQuotaPolicy.FreshnessHorizon(input.EffectiveCaptureIntervalSeconds).TotalSeconds)
                && (!window.ResetsUtc.HasValue || window.ResetsUtc > nowUtc);
        }
    }
}
