namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>Wait-time text shared by the TUI mission and vessel screens; matches the dashboard's wording.</summary>
    public static class MissionWaitText
    {
        private static readonly HashSet<MissionWaitGateEnum> _Capacity = new HashSet<MissionWaitGateEnum>
        {
            MissionWaitGateEnum.VesselVoyageSlot, MissionWaitGateEnum.VoyageMissionSlot, MissionWaitGateEnum.BranchLock, MissionWaitGateEnum.GlobalSlot,
            MissionWaitGateEnum.CaptainSlot, MissionWaitGateEnum.EndpointSlot, MissionWaitGateEnum.RuntimeSlot, MissionWaitGateEnum.Placement
        };

        /// <summary>Display name of a gate.</summary>
        /// <param name="gate">Gate.</param>
        /// <returns>Label.</returns>
        public static string GateLabel(MissionWaitGateEnum gate) => gate switch
        {
            MissionWaitGateEnum.Dependency => "Dependency",
            MissionWaitGateEnum.Parent => "Parent mission",
            MissionWaitGateEnum.IntegrationBarrier => "Other pipelines",
            MissionWaitGateEnum.BroadScope => "Broad-scope mission",
            MissionWaitGateEnum.BranchLock => "Branch lock",
            MissionWaitGateEnum.VesselVoyageSlot => "Vessel voyage slot",
            MissionWaitGateEnum.VoyageMissionSlot => "Voyage mission slot",
            MissionWaitGateEnum.CaptainSlot => "Captain slot",
            MissionWaitGateEnum.EndpointSlot => "Endpoint slot",
            MissionWaitGateEnum.RuntimeSlot => "Runtime slot",
            MissionWaitGateEnum.GlobalSlot => "Global mission limit",
            MissionWaitGateEnum.Placement => "Harbor placement",
            MissionWaitGateEnum.LaunchBackoff => "Automatic retry",
            _ => "Other"
        };

        /// <summary>Compact duration: 45s, 12m, 1h 5m, 2d 3h.</summary>
        /// <param name="seconds">Seconds.</param>
        /// <returns>Text.</returns>
        public static string Duration(double seconds)
        {
            long total = (long)Math.Max(0, Math.Round(seconds));
            if (total < 60) return total + "s";
            long minutes = total / 60;
            if (minutes < 60) return minutes + "m";
            long hours = minutes / 60;
            if (hours < 24) return minutes % 60 == 0 ? hours + "h" : hours + "h " + (minutes % 60) + "m";
            long days = hours / 24;
            return hours % 24 == 0 ? days + "d" : days + "d " + (hours % 24) + "h";
        }

        /// <summary>Seconds of an interval; an open one counts to <paramref name="nowUtc"/>.</summary>
        /// <param name="interval">Interval.</param>
        /// <param name="nowUtc">Now.</param>
        /// <returns>Seconds.</returns>
        public static double Seconds(MissionWaitInterval interval, DateTime nowUtc)
        {
            if (interval == null) throw new ArgumentNullException(nameof(interval));
            return Math.Max(0, ((interval.EndedUtc ?? nowUtc) - interval.StartedUtc).TotalSeconds);
        }

        /// <summary>
        /// "Waited 32m before starting (Parent mission 20m, Captain slot 9m, Voyage mission slot 3m)", or "Waiting 12m so
        /// far, now on Captain slot (...)" while a wait is open; null without intervals.
        /// </summary>
        /// <param name="intervals">The mission's waits.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="translate">Translator for gate labels.</param>
        /// <returns>Summary line, or null.</returns>
        public static string? Summary(IReadOnlyList<MissionWaitInterval> intervals, DateTime nowUtc, Func<string, string> translate)
        {
            if (intervals == null || intervals.Count == 0) return null;
            if (translate == null) throw new ArgumentNullException(nameof(translate));
            List<KeyValuePair<MissionWaitGateEnum, double>> byGate = intervals.GroupBy(i => i.Gate)
                .Select(g => new KeyValuePair<MissionWaitGateEnum, double>(g.Key, g.Sum(i => Seconds(i, nowUtc))))
                .Where(p => p.Value >= 1).OrderByDescending(p => p.Value).ToList();
            double total = byGate.Sum(p => p.Value);
            string breakdown = String.Join(", ", byGate.Take(4).Select(p => translate(GateLabel(p.Key)) + " " + Duration(p.Value)))
                + (byGate.Count > 4 ? ", +" + (byGate.Count - 4) : "");
            MissionWaitInterval? open = intervals.Where(i => !i.EndedUtc.HasValue).OrderByDescending(i => i.StartedUtc).FirstOrDefault();
            return open != null
                ? translate("Waiting") + " " + Duration(total) + " " + translate("so far, now on") + " " + translate(GateLabel(open.Gate)) + " (" + breakdown + ")"
                : translate("Waited") + " " + Duration(total) + " " + translate("before starting") + " (" + breakdown + ")";
        }

        /// <summary>Slot wait from a mission's timestamps, or null before assignment.</summary>
        /// <param name="readyUtc">First capacity hold.</param>
        /// <param name="assignedUtc">Assignment.</param>
        /// <returns>Seconds, or null.</returns>
        public static double? SlotWait(DateTime? readyUtc, DateTime? assignedUtc)
        {
            if (!readyUtc.HasValue || !assignedUtc.HasValue) return null;
            return Math.Max(0, (assignedUtc.Value - readyUtc.Value).TotalSeconds);
        }

        /// <summary>A recorded detail code as words: "ParentNotFinished" reads "Parent not finished"; other text is kept.</summary>
        /// <param name="detail">Recorded detail.</param>
        /// <returns>Readable text, or null.</returns>
        public static string? Detail(string? detail)
        {
            if (String.IsNullOrWhiteSpace(detail)) return null;
            if (detail.Contains(' ') || !detail.All(Char.IsLetterOrDigit)) return detail;
            System.Text.StringBuilder words = new System.Text.StringBuilder();
            for (int i = 0; i < detail.Length; i++)
            {
                char c = detail[i];
                if (i > 0 && Char.IsUpper(c) && !Char.IsUpper(detail[i - 1])) { words.Append(' '); words.Append(Char.ToLowerInvariant(c)); }
                else words.Append(c);
            }
            return words.ToString();
        }

        /// <summary>True for gates whose time is slot wait.</summary>
        /// <param name="gate">Gate.</param>
        /// <returns>True for capacity gates.</returns>
        public static bool IsCapacity(MissionWaitGateEnum gate) => _Capacity.Contains(gate);
    }
}
