namespace Armada.Core.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;

    /// <summary>
    /// What the Harbor app shows about its capability flags (RS-7.13, RS-12.19): the flags this Harbor advertises, the
    /// flags the Admiral expects that it lacks, and the update prompt when any are missing.
    /// </summary>
    public class HarborCapabilityReport
    {
        #region Public-Members

        /// <summary>
        /// Flags this Harbor advertises, in declaration order.
        /// </summary>
        public List<string> Advertised { get; } = new List<string>();

        /// <summary>
        /// Flags the Admiral expects that this Harbor does not advertise.
        /// </summary>
        public List<string> Missing { get; } = new List<string>();

        /// <summary>
        /// True when the Admiral expects a flag this Harbor lacks.
        /// </summary>
        public bool UpdateRecommended => Missing.Count > 0;

        /// <summary>
        /// One-line summary, such as "Harbor features: 13 advertised." or "... 2 the Admiral expects are missing.".
        /// </summary>
        public string Summary
        {
            get
            {
                string text = "Harbor features: " + Advertised.Count + " advertised.";
                if (Missing.Count > 0) text += " " + Missing.Count + " the Admiral expects " + (Missing.Count == 1 ? "is" : "are") + " missing.";
                return text;
            }
        }

        /// <summary>
        /// The update prompt, or null when the Harbor is current.
        /// </summary>
        public string? UpdatePrompt
        {
            get
            {
                if (Missing.Count == 0) return null;
                return "Harbor update recommended. The Admiral is newer than this Harbor and expects "
                    + String.Join(", ", Missing.Select(HarborCapabilityCatalog.Describe))
                    + ". Until you install a newer Harbor, work sent here runs the earlier behavior for "
                    + (Missing.Count == 1 ? "it" : "them") + ".";
            }
        }

        /// <summary>
        /// One line per declared flag: whether this Harbor has it, the Admiral expects it, and what it does.
        /// </summary>
        public List<string> Details
        {
            get
            {
                List<string> lines = new List<string>();
                HashSet<string> advertised = new HashSet<string>(Advertised, StringComparer.OrdinalIgnoreCase);
                HashSet<string> missing = new HashSet<string>(Missing, StringComparer.OrdinalIgnoreCase);
                foreach (string flag in HarborCapabilityFlags.All)
                {
                    string state = missing.Contains(flag) ? "missing (update recommended)" : (advertised.Contains(flag) ? "yes" : "not used");
                    lines.Add(HarborCapabilityCatalog.Describe(flag) + " [" + flag + "]: " + state);
                }

                foreach (string flag in Missing)
                    if (!HarborCapabilityFlags.All.Contains(flag, StringComparer.OrdinalIgnoreCase)) lines.Add(flag + ": missing (update recommended)");
                return lines;
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborCapabilityReport()
        {
        }

        /// <summary>
        /// Build a report from the capabilities this Harbor advertises and the flags the Admiral's handshake
        /// acknowledgement listed as missing.
        /// </summary>
        /// <param name="capabilities">Capabilities this Harbor advertises; null advertises none.</param>
        /// <param name="missing">Flags the Admiral listed as missing, or null when none are.</param>
        /// <returns>The report.</returns>
        public static HarborCapabilityReport Build(IEnumerable<HarborCapability>? capabilities, IEnumerable<string>? missing)
        {
            HarborCapabilityReport report = new HarborCapabilityReport();
            foreach (string flag in HarborCapabilityFlags.All)
                if (HarborCapabilityFlags.Supports(capabilities, flag)) report.Advertised.Add(flag);
            if (missing != null)
            {
                foreach (string flag in missing)
                    if (!String.IsNullOrWhiteSpace(flag) && !report.Missing.Contains(flag, StringComparer.OrdinalIgnoreCase)) report.Missing.Add(flag);
            }

            return report;
        }

        #endregion
    }
}
