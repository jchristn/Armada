namespace Armada.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;

    /// <summary>
    /// What startup found in the schema_migrations ledger: the applied maximum and every migration this build declares
    /// below it that has no ledger row. Startup applies only migrations above the applied maximum, so a missing row is
    /// never re-run; the schema may lack that migration's changes. A version number this build does not declare (a
    /// retired number, or a row above every declared migration) is not a gap. A gap is reported, never fatal: it is
    /// logged as a warning at startup and shown by the "Migration Ledger" doctor check.
    /// </summary>
    public class MigrationLedgerReport
    {
        #region Public-Members

        /// <summary>
        /// Doctor check name.
        /// </summary>
        public const string CheckName = "Migration Ledger";

        /// <summary>
        /// Provider label, for example "SQLite" or "SQL Server".
        /// </summary>
        public string Provider
        {
            get => _Provider;
            set => _Provider = String.IsNullOrEmpty(value) ? throw new ArgumentNullException(nameof(Provider)) : value;
        }

        /// <summary>
        /// Highest version recorded in schema_migrations when startup read the ledger. Zero for an empty ledger.
        /// </summary>
        public int AppliedMaximum { get; set; } = 0;

        /// <summary>
        /// Declared migration versions below <see cref="AppliedMaximum"/> with no ledger row, ascending. Empty when
        /// the ledger is complete. Never null.
        /// </summary>
        public List<int> MissingVersions
        {
            get => _MissingVersions;
            set => _MissingVersions = value ?? new List<int>();
        }

        /// <summary>
        /// True when at least one declared version is missing below the applied maximum.
        /// </summary>
        public bool HasGaps => _MissingVersions.Count > 0;

        /// <summary>
        /// Doctor status: Warn when the ledger has gaps, Pass otherwise.
        /// </summary>
        public DoctorCheckStatusEnum DoctorStatus => HasGaps ? DoctorCheckStatusEnum.Warn : DoctorCheckStatusEnum.Pass;

        /// <summary>
        /// Doctor and log message: the missing versions and what they mean, or that the ledger is complete.
        /// </summary>
        public string Message
        {
            get
            {
                if (!HasGaps)
                    return Provider + " schema_migrations has a row for every declared migration up to version " + AppliedMaximum + ".";

                return Provider + " schema_migrations has no row for declared migration version"
                    + (_MissingVersions.Count == 1 ? " " : "s ")
                    + String.Join(", ", _MissingVersions)
                    + " below the applied maximum " + AppliedMaximum
                    + ". Startup applies only migrations above the maximum, so these were not run and the schema may lack their changes."
                    + " Compare the schema with a fresh database or restore a backup taken before the gap; Armada keeps running.";
            }
        }

        #endregion

        #region Private-Members

        private string _Provider = "Database";
        private List<int> _MissingVersions = new List<int>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MigrationLedgerReport()
        {
        }

        /// <summary>
        /// Compare the declared migrations with the versions recorded in schema_migrations.
        /// </summary>
        /// <param name="provider">Provider label included in the message.</param>
        /// <param name="declaredMigrations">Migrations this build declares.</param>
        /// <param name="appliedVersions">Versions present in schema_migrations.</param>
        /// <returns>The report; <see cref="HasGaps"/> is false for an empty or complete ledger.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null or the provider label is empty.</exception>
        public static MigrationLedgerReport Build(
            string provider,
            IReadOnlyList<SchemaMigration> declaredMigrations,
            IReadOnlyList<int> appliedVersions)
        {
            if (String.IsNullOrEmpty(provider)) throw new ArgumentNullException(nameof(provider));
            ArgumentNullException.ThrowIfNull(declaredMigrations);
            ArgumentNullException.ThrowIfNull(appliedVersions);

            MigrationLedgerReport report = new MigrationLedgerReport();
            report.Provider = provider;
            report.AppliedMaximum = appliedVersions.Count == 0 ? 0 : Math.Max(0, appliedVersions.Max());
            if (report.AppliedMaximum == 0) return report;

            HashSet<int> applied = new HashSet<int>(appliedVersions);
            report.MissingVersions = declaredMigrations
                .Select(m => m.Version)
                .Where(v => v < report.AppliedMaximum && !applied.Contains(v))
                .Distinct()
                .OrderBy(v => v)
                .ToList();
            return report;
        }

        #endregion
    }
}
