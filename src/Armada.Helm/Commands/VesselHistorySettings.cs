namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Globalization;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core;

    /// <summary>
    /// Settings for the vessel history command.
    /// </summary>
    public class VesselHistorySettings : BaseSettings
    {
        #region Public-Members

        /// <summary>
        /// Vessel name or ID.
        /// </summary>
        [Description("Vessel name or ID")]
        [CommandArgument(0, "<vessel>")]
        public string Vessel { get; set; } = String.Empty;

        /// <summary>
        /// Branch to read (default: the vessel's default branch).
        /// </summary>
        [Description("Branch to read (default: the vessel's default branch)")]
        [CommandOption("--branch|-b")]
        public string? Branch { get; set; }

        /// <summary>
        /// Only commits strictly before this date or instant.
        /// </summary>
        [Description("Only commits before this date (yyyy-MM-dd, the start of that day in UTC) or ISO 8601 instant")]
        [CommandOption("--before")]
        public string? Before { get; set; }

        /// <summary>
        /// Commits per page.
        /// </summary>
        [Description("Commits per page, 1-200 (default 50)")]
        [CommandOption("--limit|-n")]
        public int? Limit { get; set; }

        /// <summary>
        /// Follow every page to the first commit.
        /// </summary>
        [Description("Page through the whole history")]
        [CommandOption("--all")]
        public bool All { get; set; } = false;

        /// <summary>
        /// Print the commit activity heatmap instead of the commit list.
        /// </summary>
        [Description("Print the commit activity heatmap (last year, or --from/--to)")]
        [CommandOption("--heatmap")]
        public bool Heatmap { get; set; } = false;

        /// <summary>
        /// First heatmap day.
        /// </summary>
        [Description("Heatmap first day, yyyy-MM-dd (default: 364 days before --to)")]
        [CommandOption("--from")]
        public string? From { get; set; }

        /// <summary>
        /// Last heatmap day.
        /// </summary>
        [Description("Heatmap last day, yyyy-MM-dd (default: today)")]
        [CommandOption("--to")]
        public string? To { get; set; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate option combinations and formats before any request is made.
        /// </summary>
        /// <returns>Validation result.</returns>
        public override ValidationResult Validate()
        {
            if (String.IsNullOrWhiteSpace(Vessel)) return ValidationResult.Error("A vessel name or ID is required.");
            if (Limit.HasValue && (Limit.Value < 1 || Limit.Value > Constants.VesselHistoryMaxPageSize))
                return ValidationResult.Error("--limit must be between 1 and " + Constants.VesselHistoryMaxPageSize + ".");
            if (!Heatmap && (!String.IsNullOrEmpty(From) || !String.IsNullOrEmpty(To)))
                return ValidationResult.Error("--from and --to apply to --heatmap; use --before to jump the commit list to a date.");
            if (Heatmap && (All || !String.IsNullOrEmpty(Before) || Limit.HasValue))
                return ValidationResult.Error("--all, --before, and --limit apply to the commit list, not --heatmap.");
            if (!String.IsNullOrEmpty(From) && !IsDay(From)) return ValidationResult.Error("--from must be a date in yyyy-MM-dd form.");
            if (!String.IsNullOrEmpty(To) && !IsDay(To)) return ValidationResult.Error("--to must be a date in yyyy-MM-dd form.");
            return ValidationResult.Success();
        }

        #endregion

        #region Private-Methods

        private static bool IsDay(string value)
        {
            return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime _);
        }

        #endregion
    }
}
