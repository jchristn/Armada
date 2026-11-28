namespace Armada.Core.Database
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Migration 133 (RELIABILITY_SIMPLICITY_PLAN.md RS-3.1): typed mission columns for state that was kept as text in
    /// the description, backfilled from the old markers so an upgraded database keeps its escalations and fan-outs.
    /// </summary>
    internal static class MissionTypedStateSchema
    {
        #region Internal-Members

        internal const int Version = 133;

        /// <summary>Marker the escalation path appended to a mission description before 1.5.0.</summary>
        internal const string LegacyEscalatedHeading = "## Escalated to ";

        /// <summary>Marker the fan-out appended to an Architect description before 1.5.0.</summary>
        internal const string LegacyFanOutCompleteMarker = "<!-- armada:fanout-complete -->";

        /// <summary>Marker the fan-out prepended to each Architect slice description before 1.5.0.</summary>
        internal const string LegacyArchitectHandoffMarker = "<!-- ARMADA:ARCHITECT-HANDOFF -->";

        #endregion

        #region Private-Members

        private static readonly string[] _Columns = new string[]
        {
            "escalation_count", "escalated_from_captain_id", "escalated_from_model", "fan_out_completed_utc",
            "architect_source_mission_id", "architect_plan_index", "launch_intent_utc", "launch_job_id", "notes_json"
        };

        #endregion

        #region Internal-Methods

        internal static SchemaMigration Migration(DatabaseTypeEnum provider)
        {
            const string description = "Add typed escalation, fan-out, launch and note columns to missions and backfill them from the description markers";
            List<string> statements = new List<string>();
            switch (provider)
            {
                case DatabaseTypeEnum.Sqlite:
                    statements.Add("ALTER TABLE missions ADD COLUMN escalation_count INTEGER NOT NULL DEFAULT 0;");
                    statements.Add("ALTER TABLE missions ADD COLUMN escalated_from_captain_id TEXT NULL;");
                    statements.Add("ALTER TABLE missions ADD COLUMN escalated_from_model TEXT NULL;");
                    statements.Add("ALTER TABLE missions ADD COLUMN fan_out_completed_utc TEXT NULL;");
                    statements.Add("ALTER TABLE missions ADD COLUMN architect_source_mission_id TEXT NULL;");
                    statements.Add("ALTER TABLE missions ADD COLUMN architect_plan_index INTEGER NULL;");
                    statements.Add("ALTER TABLE missions ADD COLUMN launch_intent_utc TEXT NULL;");
                    statements.Add("ALTER TABLE missions ADD COLUMN launch_job_id TEXT NULL;");
                    statements.Add("ALTER TABLE missions ADD COLUMN notes_json TEXT NULL;");
                    AddPortableBackfill(statements);
                    break;
                case DatabaseTypeEnum.Postgresql:
                    statements.Add("ALTER TABLE missions ADD COLUMN IF NOT EXISTS escalation_count INTEGER NOT NULL DEFAULT 0, "
                        + "ADD COLUMN IF NOT EXISTS escalated_from_captain_id TEXT NULL, ADD COLUMN IF NOT EXISTS escalated_from_model TEXT NULL, "
                        + "ADD COLUMN IF NOT EXISTS fan_out_completed_utc TIMESTAMP NULL, ADD COLUMN IF NOT EXISTS architect_source_mission_id TEXT NULL, "
                        + "ADD COLUMN IF NOT EXISTS architect_plan_index INTEGER NULL, ADD COLUMN IF NOT EXISTS launch_intent_utc TIMESTAMP NULL, "
                        + "ADD COLUMN IF NOT EXISTS launch_job_id TEXT NULL, ADD COLUMN IF NOT EXISTS notes_json TEXT NULL;");
                    AddPortableBackfill(statements);
                    break;
                case DatabaseTypeEnum.SqlServer:
                    statements.Add("ALTER TABLE missions ADD escalation_count INT NOT NULL CONSTRAINT DF_missions_escalation_count DEFAULT 0, "
                        + "escalated_from_captain_id NVARCHAR(256) NULL, escalated_from_model NVARCHAR(256) NULL, fan_out_completed_utc DATETIME2 NULL, "
                        + "architect_source_mission_id NVARCHAR(256) NULL, architect_plan_index INT NULL, launch_intent_utc DATETIME2 NULL, "
                        + "launch_job_id NVARCHAR(256) NULL, notes_json NVARCHAR(MAX) NULL;");
                    AddPortableBackfill(statements);
                    break;
                case DatabaseTypeEnum.Mysql:
                    statements.Add("ALTER TABLE missions ADD COLUMN escalation_count INT NOT NULL DEFAULT 0, "
                        + "ADD COLUMN escalated_from_captain_id VARCHAR(256) NULL, ADD COLUMN escalated_from_model VARCHAR(256) NULL, "
                        + "ADD COLUMN fan_out_completed_utc DATETIME(6) NULL, ADD COLUMN architect_source_mission_id VARCHAR(256) NULL, "
                        + "ADD COLUMN architect_plan_index INT NULL, ADD COLUMN launch_intent_utc DATETIME(6) NULL, ADD COLUMN launch_job_id VARCHAR(256) NULL, ADD COLUMN notes_json LONGTEXT NULL;");
                    statements.Add(EscalationBackfill());
                    statements.Add(FanOutBackfill());
                    statements.Add(SliceSourceBackfill());
                    // MySQL cannot read the table an UPDATE writes in a subquery, so the index is computed in a derived table.
                    statements.Add("UPDATE missions m JOIN (SELECT s.id AS slice_id, (SELECT COUNT(*) FROM missions e WHERE e.architect_source_mission_id = s.architect_source_mission_id "
                        + "AND (e.created_utc < s.created_utc OR (e.created_utc = s.created_utc AND e.id < s.id))) AS plan_index "
                        + "FROM missions s WHERE s.architect_source_mission_id IS NOT NULL) x ON x.slice_id = m.id "
                        + "SET m.architect_plan_index = x.plan_index WHERE m.architect_plan_index IS NULL;");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported database provider.");
            }

            return new SchemaMigration(Version, description, statements.ToArray()) { AlreadyAppliedCheckSql = AlreadyAppliedCheck(provider) };
        }

        #endregion

        #region Private-Methods

        private static void AddPortableBackfill(List<string> statements)
        {
            statements.Add(EscalationBackfill());
            statements.Add(FanOutBackfill());
            statements.Add(SliceSourceBackfill());
            // A slice's plan index is its creation order among its Architect's slices: the first slice is the Worker the
            // pipeline created with the voyage, and the rest were created in plan order.
            statements.Add("UPDATE missions SET architect_plan_index = (SELECT COUNT(*) FROM missions e WHERE e.architect_source_mission_id = missions.architect_source_mission_id "
                + "AND (e.created_utc < missions.created_utc OR (e.created_utc = missions.created_utc AND e.id < missions.id))) "
                + "WHERE architect_source_mission_id IS NOT NULL AND architect_plan_index IS NULL;");
        }

        private static string EscalationBackfill()
        {
            return "UPDATE missions SET escalation_count = 1 WHERE escalation_count = 0 AND description LIKE '%" + LegacyEscalatedHeading + "%';";
        }

        private static string FanOutBackfill()
        {
            return "UPDATE missions SET fan_out_completed_utc = last_update_utc WHERE fan_out_completed_utc IS NULL AND description LIKE '%" + LegacyFanOutCompleteMarker + "%';";
        }

        private static string SliceSourceBackfill()
        {
            // Every slice, the first included, depends on the Architect that planned it.
            return "UPDATE missions SET architect_source_mission_id = depends_on_mission_id WHERE architect_source_mission_id IS NULL "
                + "AND depends_on_mission_id IS NOT NULL AND persona = 'Worker' AND description LIKE '%" + LegacyArchitectHandoffMarker + "%';";
        }

        private static string AlreadyAppliedCheck(DatabaseTypeEnum provider)
        {
            string names = "'" + String.Join("','", _Columns) + "'";
            string count = _Columns.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (provider == DatabaseTypeEnum.Sqlite)
                return "SELECT CASE WHEN COUNT(*) = " + count + " THEN 1 ELSE 0 END FROM pragma_table_info('missions') WHERE name IN (" + names + ")";
            if (provider == DatabaseTypeEnum.SqlServer)
                return "SELECT CASE WHEN COUNT(*) = " + count + " THEN 1 ELSE 0 END FROM sys.columns WHERE object_id = OBJECT_ID('missions') AND name IN (" + names + ")";
            return "SELECT CASE WHEN COUNT(*) = " + count + " THEN 1 ELSE 0 END FROM information_schema.columns WHERE table_name = 'missions' AND column_name IN (" + names + ") AND "
                + (provider == DatabaseTypeEnum.Mysql ? "table_schema = DATABASE()" : "table_schema = current_schema()");
        }

        #endregion
    }
}
