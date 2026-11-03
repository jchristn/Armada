namespace Armada.Core.Database
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>Structured selection-ledger migration shared by the supported SQL dialects.</summary>
    internal static class UsageSelectionSchema
    {
        internal static SchemaMigration CaptureTimingMigration(DatabaseTypeEnum provider)
        {
            string check = provider == DatabaseTypeEnum.Sqlite ? "SELECT COUNT(*) FROM pragma_table_info('usage_snapshots') WHERE name='probe_started_utc'"
                : provider == DatabaseTypeEnum.SqlServer ? "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('usage_snapshots') AND name='probe_started_utc'"
                : "SELECT COUNT(*) FROM information_schema.columns WHERE table_name='usage_snapshots' AND column_name='probe_started_utc' AND "
                    + (provider == DatabaseTypeEnum.Mysql ? "table_schema=DATABASE()" : "table_schema=current_schema()");
            return new SchemaMigration(124, "Preserve usage capture start timing for conservative debt settlement",
                "ALTER TABLE usage_snapshots ADD " + (provider == DatabaseTypeEnum.Sqlite || provider == DatabaseTypeEnum.Postgresql ? "COLUMN " : "")
                + "probe_started_utc VARCHAR(40) NULL;") { AlreadyAppliedCheckSql = check };
        }

        internal static SchemaMigration Migration(DatabaseTypeEnum provider)
        {
            bool sqlServer = provider == DatabaseTypeEnum.SqlServer;
            List<string> statements = new List<string>();
            string Prefix(string name) => sqlServer ? "IF OBJECT_ID('" + name + "','U') IS NULL " : "";
            string Create(string name) => Prefix(name) + "CREATE TABLE " + (sqlServer ? "" : "IF NOT EXISTS ") + name;
            void Index(string name, string table, string columns, bool unique = false)
            {
                string condition = sqlServer ? "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='" + name + "' AND object_id=OBJECT_ID('" + table + "')) " : "";
                statements.Add(condition + "CREATE " + (unique ? "UNIQUE " : "") + "INDEX " + (sqlServer || provider == DatabaseTypeEnum.Mysql ? "" : "IF NOT EXISTS ") + name + " ON " + table + "(" + columns + ");");
            }
            statements.Add(Create("captain_selection_policy_revisions") + @"(id VARCHAR(64) NOT NULL PRIMARY KEY,
                tenant_id VARCHAR(64) NOT NULL,
                user_id VARCHAR(64) NULL,
                created_utc VARCHAR(40) NOT NULL,
                last_update_utc VARCHAR(40) NOT NULL,
                active INTEGER NOT NULL,
                fingerprint VARCHAR(64) NOT NULL,
                algorithm_version VARCHAR(512) NOT NULL,
                mode VARCHAR(64) NOT NULL,
                strategy VARCHAR(64) NOT NULL,
                unknown_usage_behavior VARCHAR(64) NOT NULL,
                reserve_percent DECIMAL(18,6) NOT NULL,
                comfortable_headroom_percent DECIMAL(18,6) NOT NULL,
                per_attempt_reservation_percent DECIMAL(18,6) NOT NULL,
                maximum_freshness_seconds INTEGER NOT NULL,
                unknown_retry_seconds INTEGER NOT NULL,
                provider_cooldown_seconds INTEGER NOT NULL,
                settlement_lag_seconds INTEGER NOT NULL,
                maximum_unsettled_seconds INTEGER NOT NULL,
                decision_retention_days INTEGER NOT NULL,
                maximum_candidates INTEGER NOT NULL,
                UNIQUE(tenant_id, fingerprint),
                UNIQUE(tenant_id,id));");
            statements.Add(Create("captain_selection_decisions") + @"(id VARCHAR(64) NOT NULL PRIMARY KEY,
                tenant_id VARCHAR(64) NOT NULL,
                user_id VARCHAR(64) NULL,
                created_utc VARCHAR(40) NOT NULL,
                last_update_utc VARCHAR(40) NOT NULL,
                active INTEGER NOT NULL,
                mission_id VARCHAR(64) NOT NULL,
                voyage_id VARCHAR(64) NULL,
                execution_attempt_id VARCHAR(64) NOT NULL,
                trigger_kind VARCHAR(512) NOT NULL,
                mode VARCHAR(64) NOT NULL,
                outcome VARCHAR(512) NOT NULL,
                selected_captain_id VARCHAR(64) NULL,
                selected_provider VARCHAR(512) NULL,
                selected_model VARCHAR(512) NULL,
                hypothetical_captain_id VARCHAR(64) NULL,
                hypothetical_outcome VARCHAR(512) NULL,
                algorithm_version VARCHAR(512) NOT NULL,
                policy_revision_id VARCHAR(64) NOT NULL,
                routing_revision VARCHAR(512) NULL,
                evidence_fingerprint VARCHAR(64) NOT NULL,
                previous_decision_id VARCHAR(64) NULL,
                evaluated_utc VARCHAR(40) NOT NULL,
                next_reconsider_utc VARCHAR(40) NULL,
                primary_reason_code VARCHAR(512) NOT NULL,
                override_actor_id VARCHAR(64) NULL,
                override_reason VARCHAR(512) NULL,
                candidate_count INTEGER NOT NULL,
                UNIQUE(tenant_id, execution_attempt_id),
                UNIQUE(tenant_id,id),
                FOREIGN KEY(tenant_id,policy_revision_id) REFERENCES captain_selection_policy_revisions(tenant_id,id));");
            statements.Add(Create("captain_selection_candidates") + @"(id VARCHAR(64) NOT NULL PRIMARY KEY,
                tenant_id VARCHAR(64) NOT NULL,
                user_id VARCHAR(64) NULL,
                created_utc VARCHAR(40) NOT NULL,
                last_update_utc VARCHAR(40) NOT NULL,
                active INTEGER NOT NULL,
                decision_id VARCHAR(64) NOT NULL,
                captain_id VARCHAR(64) NOT NULL,
                provider VARCHAR(512) NULL,
                configured_model VARCHAR(512) NULL,
                routing_bucket INTEGER NOT NULL,
                rank_number INTEGER NOT NULL,
                band VARCHAR(64) NOT NULL,
                primary_reason_code VARCHAR(512) NOT NULL,
                eligible INTEGER NOT NULL,
                occupied_slots INTEGER NOT NULL,
                capacity INTEGER NOT NULL,
                last_assigned_utc VARCHAR(40) NULL,
                membership_id VARCHAR(64) NULL,
                host_id VARCHAR(64) NULL,
                runtime VARCHAR(64) NULL,
                account_key VARCHAR(64) NULL,
                available_percent DECIMAL(18,6) NULL,
                UNIQUE(tenant_id,id),
                FOREIGN KEY(tenant_id,decision_id) REFERENCES captain_selection_decisions(tenant_id,id) ON DELETE CASCADE);");
            statements.Add(Create("captain_selection_candidate_reasons") + @"(id VARCHAR(64) NOT NULL PRIMARY KEY,
                tenant_id VARCHAR(64) NOT NULL,
                user_id VARCHAR(64) NULL,
                created_utc VARCHAR(40) NOT NULL,
                last_update_utc VARCHAR(40) NOT NULL,
                active INTEGER NOT NULL,
                decision_id VARCHAR(64) NOT NULL,
                candidate_id VARCHAR(64) NOT NULL,
                ordinal_number INTEGER NOT NULL,
                reason_code VARCHAR(512) NOT NULL,
                required_percent DECIMAL(18,6) NULL,
                available_percent DECIMAL(18,6) NULL,
                blocker_id VARCHAR(64) NULL,
                UNIQUE(tenant_id,id),
                FOREIGN KEY(tenant_id,decision_id) REFERENCES captain_selection_decisions(tenant_id,id) ON DELETE CASCADE);");
            statements.Add(Create("captain_selection_windows") + @"(id VARCHAR(64) NOT NULL PRIMARY KEY,
                tenant_id VARCHAR(64) NOT NULL,
                user_id VARCHAR(64) NULL,
                created_utc VARCHAR(40) NOT NULL,
                last_update_utc VARCHAR(40) NOT NULL,
                active INTEGER NOT NULL,
                decision_id VARCHAR(64) NOT NULL,
                candidate_id VARCHAR(64) NOT NULL,
                snapshot_id VARCHAR(64) NULL,
                captured_utc VARCHAR(40) NULL,
                probe_started_utc VARCHAR(40) NULL,
                source VARCHAR(64) NULL,
                status VARCHAR(64) NULL,
                configuration_revision VARCHAR(512) NULL,
                window_key VARCHAR(64) NOT NULL,
                window_name VARCHAR(512) NULL,
                model VARCHAR(512) NULL,
                scope_name VARCHAR(512) NULL,
                generation_key VARCHAR(64) NULL,
                used_percent DECIMAL(18,6) NULL,
                used_amount BIGINT NULL,
                limit_amount BIGINT NULL,
                unit VARCHAR(512) NULL,
                resets_utc VARCHAR(40) NULL,
                applicable INTEGER NOT NULL,
                active_reservation_percent DECIMAL(18,6) NOT NULL,
                unsettled_percent DECIMAL(18,6) NOT NULL,
                reserve_percent DECIMAL(18,6) NOT NULL,
                UNIQUE(tenant_id,id),
                FOREIGN KEY(tenant_id,decision_id) REFERENCES captain_selection_decisions(tenant_id,id) ON DELETE CASCADE);");
            statements.Add(Create("usage_budget_reservations") + @"(id VARCHAR(64) NOT NULL PRIMARY KEY,
                tenant_id VARCHAR(64) NOT NULL,
                user_id VARCHAR(64) NULL,
                created_utc VARCHAR(40) NOT NULL,
                last_update_utc VARCHAR(40) NOT NULL,
                active INTEGER NOT NULL,
                decision_id VARCHAR(64) NOT NULL,
                mission_id VARCHAR(64) NOT NULL,
                execution_attempt_id VARCHAR(64) NOT NULL,
                captain_id VARCHAR(64) NOT NULL,
                membership_id VARCHAR(64) NOT NULL,
                host_id VARCHAR(64) NOT NULL,
                runtime VARCHAR(64) NOT NULL,
                account_key VARCHAR(64) NOT NULL,
                window_key VARCHAR(64) NOT NULL,
                generation_key VARCHAR(64) NOT NULL,
                reserved_percent DECIMAL(18,6) NOT NULL,
                state VARCHAR(64) NOT NULL,
                revision INTEGER NOT NULL,
                execution_started_utc VARCHAR(40) NULL,
                execution_ended_utc VARCHAR(40) NULL,
                settlement_eligible_utc VARCHAR(40) NULL,
                settled_by_snapshot_id VARCHAR(64) NULL,
                released_utc VARCHAR(40) NULL,
                transition_reason VARCHAR(512) NULL,
                UNIQUE(tenant_id, execution_attempt_id, window_key, generation_key),
                UNIQUE(tenant_id,id),
                FOREIGN KEY(tenant_id,decision_id) REFERENCES captain_selection_decisions(tenant_id,id) ON DELETE CASCADE);");
            statements.Add(Create("captain_selection_execution_events") + @"(id VARCHAR(64) NOT NULL PRIMARY KEY,
                tenant_id VARCHAR(64) NOT NULL,
                user_id VARCHAR(64) NULL,
                created_utc VARCHAR(40) NOT NULL,
                last_update_utc VARCHAR(40) NOT NULL,
                active INTEGER NOT NULL,
                decision_id VARCHAR(64) NOT NULL,
                execution_attempt_id VARCHAR(64) NOT NULL,
                event_kind VARCHAR(64) NOT NULL,
                actual_provider VARCHAR(512) NULL,
                actual_model VARCHAR(512) NULL,
                reporting_source VARCHAR(512) NULL,
                occurred_utc VARCHAR(40) NOT NULL,
                failure_classification VARCHAR(512) NULL,
                UNIQUE(tenant_id,id),
                FOREIGN KEY(tenant_id,decision_id) REFERENCES captain_selection_decisions(tenant_id,id) ON DELETE CASCADE);");
            statements.Add(Create("usage_account_cooldowns") + @"(id VARCHAR(64) NOT NULL PRIMARY KEY,
                tenant_id VARCHAR(64) NOT NULL,
                user_id VARCHAR(64) NULL,
                created_utc VARCHAR(40) NOT NULL,
                last_update_utc VARCHAR(40) NOT NULL,
                active INTEGER NOT NULL,
                host_id VARCHAR(64) NOT NULL,
                runtime VARCHAR(64) NOT NULL,
                account_key VARCHAR(64) NOT NULL,
                window_key VARCHAR(64) NOT NULL,
                revision INTEGER NOT NULL,
                observed_utc VARCHAR(40) NOT NULL,
                retry_after_utc VARCHAR(40) NOT NULL,
                classification VARCHAR(512) NOT NULL,
                UNIQUE(tenant_id,id));");
            if (provider != DatabaseTypeEnum.Mysql)
            {
                Index("ix_selection_mission", "captain_selection_decisions", "tenant_id,mission_id,evaluated_utc,id");
                Index("ix_selection_outcome", "captain_selection_decisions", "tenant_id,outcome,evaluated_utc,id");
                Index("ix_selection_candidates", "captain_selection_candidates", "tenant_id,decision_id,rank_number,id");
                Index("ix_selection_captain", "captain_selection_candidates", "tenant_id,captain_id,decision_id");
                Index("ix_selection_windows", "captain_selection_windows", "tenant_id,decision_id,candidate_id,id");
                Index("ix_selection_reasons", "captain_selection_candidate_reasons", "tenant_id,decision_id,candidate_id,ordinal_number");
                Index("ix_selection_events", "captain_selection_execution_events", "tenant_id,decision_id,occurred_utc,id");
                Index("ix_usage_reservation_budget", "usage_budget_reservations", "tenant_id,host_id,runtime,account_key,window_key,state");
                Index("ix_usage_cooldown_budget", "usage_account_cooldowns", "tenant_id,host_id,runtime,account_key,window_key");
            }
            else
            {
                // MySQL CREATE INDEX has no IF NOT EXISTS; embed indexes in idempotent CREATE TABLE statements.
                void Inline(string table, string clause)
                {
                    int index = statements.FindIndex(s => s.StartsWith("CREATE TABLE IF NOT EXISTS " + table + "(", StringComparison.Ordinal));
                    statements[index] = statements[index].Substring(0, statements[index].Length - 2) + ", " + clause + ");";
                }
                Inline("captain_selection_decisions", "INDEX ix_selection_mission(tenant_id,mission_id,evaluated_utc,id), INDEX ix_selection_outcome(tenant_id,outcome,evaluated_utc,id)");
                Inline("captain_selection_candidates", "INDEX ix_selection_candidates(tenant_id,decision_id,rank_number,id), INDEX ix_selection_captain(tenant_id,captain_id,decision_id)");
                Inline("captain_selection_windows", "INDEX ix_selection_windows(tenant_id,decision_id,candidate_id,id)");
                Inline("captain_selection_candidate_reasons", "INDEX ix_selection_reasons(tenant_id,decision_id,candidate_id,ordinal_number)");
                Inline("captain_selection_execution_events", "INDEX ix_selection_events(tenant_id,decision_id,occurred_utc,id)");
                Inline("usage_budget_reservations", "INDEX ix_usage_reservation_budget(tenant_id,host_id,runtime,account_key,window_key,state)");
                Inline("usage_account_cooldowns", "INDEX ix_usage_cooldown_budget(tenant_id,host_id,runtime,account_key,window_key)");
            }
            if (sqlServer)
                for (int i = 0; i < statements.Count; i++) statements[i] = statements[i].Replace("VARCHAR(", "NVARCHAR(", StringComparison.Ordinal);
            return new SchemaMigration(123, "Add structured captain selection history and usage reservation ledger", statements.ToArray());
        }
    }
}
