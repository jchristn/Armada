namespace Armada.Tui.Screens.Activity
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;

    /// <summary>Text, thresholds and last-good age shared by terminal usage views.</summary>
    public static class UsagePresentation
    {
        /// <summary>Build a localized, searchable account/host document, including unknown and failure states.</summary>
        public static OpsDocument Build(OpsDocument doc, List<UsageSnapshotView>? views, string? error, DateTime now)
        {
            if (error != null) { doc.Field("Usage", error); if (views == null) return doc; }
            if (views == null) return doc.Field("Usage", doc.Loc.T("Loading usage..."));
            if (views.Count == 0) return doc.Field("Usage", doc.Loc.T("No usage observed yet"));
            foreach (UsageSnapshotView view in views)
            {
                doc.Section("Usage");
                doc.Field("Provider", view.Provider ?? "-");
                doc.Field("Host", view.HostId ?? "-");
                doc.Field("Account", view.AccountKey == null ? "-" : view.AccountKey.Substring(0, Math.Min(12, view.AccountKey.Length)));
                doc.Field("Status", doc.Loc.T(view.Stale ? "Stale" : view.Status == UsageSnapshotStatusEnum.AuthMissing ? "Sign-in needed" : State(view.Status)));
                if (view.Status == UsageSnapshotStatusEnum.AuthMissing) doc.Field("Host", doc.Loc.T("Sign in to the runtime on host {{host}}. Armada never asks you to paste a credential here.", LocalizationArgs.Of("host", view.HostId ?? "-")));
                doc.Field("Last observed", doc.Loc.FormatRelative(view.ObservedUtc ?? view.CapturedUtc, now));
                doc.Field("Requested interval", doc.Loc.FormatNumber(view.RequestedIntervalSeconds));
                doc.Field("Effective interval", doc.Loc.FormatNumber(view.EffectiveIntervalSeconds));
                foreach (UsageWindow window in view.Windows)
                {
                    double? percent = window.UsedPercent;
                    string label = percent >= 90 ? "Near limit" : percent >= 75 ? "Warning" : "Normal";
                    doc.Field(window.Name, percent.HasValue ? percent.Value.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + "% " + doc.Loc.T(label) : doc.Loc.T("Unknown"), percent >= 90 ? doc.Theme.Error : percent >= 75 ? doc.Theme.Warning : null);
                    if (window.ResetsUtc.HasValue) doc.Field("Resets", doc.Loc.FormatDateTime(window.ResetsUtc.Value));
                    if (window.CoverageNote != null) doc.Field("Estimated", window.CoverageNote);
                    UsageReservationEstimate? estimate = view.ReservationEstimates.FirstOrDefault(e => e.WindowName == window.Name && e.Model == window.Model
                        && e.WindowMinutes == window.WindowMinutes && (e.ActivePercent > 0 || e.UnsettledPercent > 0));
                    if (estimate != null)
                        doc.Text("  " + doc.Loc.T("usageSelection.reservations.estimate", LocalizationArgs.Of(
                            "active", estimate.ActivePercent.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + "%",
                            "unsettled", estimate.UnsettledPercent.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + "%")), doc.Theme.Muted);
                }
                if (view.ReservationUncertain) doc.Text(doc.Loc.T("usageSelection.reservations.uncertain"), doc.Theme.Muted);
                doc.Field("Captains", String.Join(", ", view.CaptainIds));
                if (view.CapabilityReason != null) doc.Field("Unsupported", view.CapabilityReason);
                if (view.ErrorCode != null) doc.Field("Capture failed", view.ErrorCode);
            }
            return doc;
        }
        /// <summary>Highest known window; unrelated accounts are never added together.</summary>
        public static string Headline(IEnumerable<UsageSnapshotView> views, DateTime now, ITextLocalizer loc)
        {
            List<UsageSnapshotView> accounts = views.ToList();
            double? highest = accounts.SelectMany(v => v.Windows).Where(w => !w.ResetsUtc.HasValue || w.ResetsUtc > now).Select(w => w.UsedPercent).Max();
            if (!highest.HasValue) return loc.T(accounts.Count == 0 ? "No usage observed yet" : State(accounts[0].Status));
            return highest.Value.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + "% " + loc.T(highest >= 90 ? "Near limit" : highest >= 75 ? "Warning" : "Normal") + (accounts.Any(v => v.Stale) ? " (" + loc.T("Stale") + ")" : "");
        }
        private static string State(UsageSnapshotStatusEnum status) => status == UsageSnapshotStatusEnum.Ok ? "Fresh" : status == UsageSnapshotStatusEnum.AuthMissing ? "Sign-in needed" : status == UsageSnapshotStatusEnum.Failed ? "Capture failed" : status.ToString();
    }
}
