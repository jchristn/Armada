namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// One keyboard flow per built list screen in Operations, Activity, and System (W8.2): open, filter, select, row
    /// menu, create form or page where the screen has one, open the row with Enter, and go back with Alt+Left.
    /// </summary>
    public sealed class TuiKeyboardFlowOpsSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Tui.KeyboardFlows.Ops";
        private const string Stamp = "\"CreatedUtc\":\"2026-10-01T00:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T00:00:00Z\"";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            foreach (TuiFlowSpec spec in Specs())
            {
                TuiFlowSpec captured = spec;
                cases.Add(TuiCase.Sync(Suite, captured.Id, "Keyboard flow: " + captured.Route + " open, filter, select, row menu, open, back", () => TuiFlowRunner.Run(captured)));
            }

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI keyboard flows per screen (Operations, Activity, System)", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static List<TuiFlowSpec> Specs()
        {
            List<TuiFlowSpec> specs = new List<TuiFlowSpec>();

            string mission = "{\"Id\":\"msn_1\",\"Title\":\"Alpha mission\",\"Status\":\"Complete\",\"VesselId\":\"vsl_1\",\"Priority\":100," + Stamp + "}";
            TuiFlowSpec missions = TuiFlowSpec.Create("missions", "/missions", "/api/v1/missions/summaries", "msn_1", "Alpha mission", "/missions/msn_1", mission);
            missions.DetailPath = "/api/v1/missions/msn_1";
            specs.Add(missions);

            string voyage = "{\"Id\":\"vyg_1\",\"Title\":\"Alpha voyage\",\"Status\":\"InProgress\"," + Stamp + "}";
            TuiFlowSpec voyages = TuiFlowSpec.Create("voyages", "/missions?tab=voyages", "/api/v1/voyages", "vyg_1", "Alpha voyage", "/voyages/vyg_1", voyage);
            voyages.DetailJson = "{\"Voyage\":" + voyage + ",\"Missions\":[" + mission + "]}";
            voyages.NewRoute = "/voyages/create";
            voyages.NewBackKey = "esc";
            specs.Add(voyages);

            TuiFlowSpec merge = TuiFlowSpec.Create("merge_queue", "/missions?tab=merge-queue", "/api/v1/merge-queue", "mrg_1", "alpha/branch", "/merge-queue/mrg_1",
                "{\"Id\":\"mrg_1\",\"BranchName\":\"alpha/branch\",\"TargetBranch\":\"main\",\"Status\":\"Queued\",\"Priority\":1,\"VesselId\":\"vsl_1\"," + Stamp + "}");
            specs.Add(merge);

            TuiFlowSpec backlog = TuiFlowSpec.Create("backlog", "/dispatch?tab=backlog", "/api/v1/backlog", "obj_1", "Alpha objective", "/backlog/obj_1",
                "{\"Id\":\"obj_1\",\"Title\":\"Alpha objective\",\"Status\":\"Draft\",\"Kind\":\"Feature\",\"Priority\":\"P2\",\"Rank\":1,\"BacklogState\":\"Inbox\",\"Effort\":\"M\",\"BlockedByObjectiveIds\":[],\"SuggestedPlaybooks\":[],\"Tags\":[],\"AcceptanceCriteria\":[],\"NonGoals\":[],\"RolloutConstraints\":[],\"EvidenceLinks\":[],\"FleetIds\":[],\"VesselIds\":[],\"PlanningSessionIds\":[],\"VoyageIds\":[],\"MissionIds\":[]," + Stamp + "}");
            backlog.NewRoute = "/backlog/new";
            backlog.NewBackKey = "esc";
            backlog.DetailBackKey = "esc";
            specs.Add(backlog);

            TuiFlowSpec runs = TuiFlowSpec.Create("fleet_action_runs", "/fleet-actions?tab=runs", "/api/v1/fleet-action-runs/enumerate", "far_1", "Alpha action", "/fleet-actions/runs/far_1",
                "{\"Id\":\"far_1\",\"ActionId\":\"fa_1\",\"ActionName\":\"Alpha action\",\"Kind\":\"Command\",\"CommandText\":\"git status\",\"Status\":\"Completed\",\"TargetCount\":1,\"CompletedCount\":1," + Stamp + "}");
            runs.ListMethod = "POST";
            runs.DetailPath = "/api/v1/fleet-action-runs/far_1";
            runs.DetailJson = "{\"Run\":" + runs.RowJson + ",\"Targets\":[]}";
            runs.NewKey = null;
            specs.Add(runs);

            TuiFlowSpec events = TuiFlowSpec.Create("events", "/activity?source=events", "/api/v1/events", "evt_1", "Alpha event", "/events/evt_1",
                "{\"Id\":\"evt_1\",\"EventType\":\"mission.completed\",\"EntityType\":\"mission\",\"EntityId\":\"msn_1\",\"MissionId\":\"msn_1\",\"Message\":\"Alpha event\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\"}");
            events.NewKey = null;
            specs.Add(events);

            TuiFlowSpec signals = TuiFlowSpec.Create("signals", "/activity?source=signals", "/api/v1/signals", "sig_1", "Alpha signal", "/signals/sig_1",
                "{\"Id\":\"sig_1\",\"Type\":\"Nudge\",\"Payload\":\"Alpha signal\",\"ToCaptainId\":\"cpt_1\",\"Read\":false,\"CreatedUtc\":\"2026-10-04T10:00:00Z\"}");
            signals.NewKey = null;
            specs.Add(signals);

            TuiFlowSpec requests = TuiFlowSpec.Create("api_requests", "/activity?source=requests", "/api/v1/request-history", "req_x", "/api/v1/alpha", null,
                "{\"Id\":\"req_x\",\"Method\":\"GET\",\"Route\":\"/api/v1/alpha\",\"StatusCode\":200,\"DurationMs\":3,\"IsSuccess\":true,\"CreatedUtc\":\"" + DateTime.UtcNow.ToString("o") + "\"}");
            requests.ExtraRoutes.Add(new[] { "GET", "/api/v1/request-history/req_x", "{\"Entry\":" + requests.RowJson + ",\"Detail\":null}" });
            requests.OpenedText = "Entry ID";
            requests.ExtraRoutes.Add(new[] { "GET", "/api/v1/request-history/summary", "{\"TotalCount\":1,\"SuccessCount\":1,\"FailureCount\":0,\"SuccessRate\":100,\"AverageDurationMs\":3,\"BucketMinutes\":15,\"Buckets\":[]}" });
            requests.NewKey = null;

            // The request list has its own filter fields (covered by Tui.RequestHistory); / is not a filter key here.
            requests.FilterKey = null;
            specs.Add(requests);

            TuiFlowSpec tenants = TuiFlowSpec.Create("tenants", "/server?tab=tenants", "/api/v1/tenants", "ten_alpha", "Alpha Tenant", null,
                "{\"Id\":\"ten_alpha\",\"Name\":\"Alpha Tenant\",\"Active\":true,\"CreatedUtc\":\"2026-10-01T00:00:00Z\"}");
            specs.Add(tenants);

            TuiFlowSpec users = TuiFlowSpec.Create("users", "/server?tab=users", "/api/v1/users", "usr_alpha", "alpha@armada", null,
                "{\"Id\":\"usr_alpha\",\"TenantId\":\"ten_default\",\"Email\":\"alpha@armada\",\"FirstName\":\"Alpha\",\"IsAdmin\":false,\"IsTenantAdmin\":false,\"Active\":true,\"CreatedUtc\":\"2026-10-01T00:00:00Z\"}");
            users.ExtraRoutes.Add(new[] { "GET", "/api/v1/tenants", "{\"Objects\":[{\"Id\":\"ten_default\",\"Name\":\"Default Tenant\",\"Active\":true}],\"TotalRecords\":1}" });
            specs.Add(users);

            TuiFlowSpec credentials = TuiFlowSpec.Create("credentials", "/server?tab=credentials", "/api/v1/credentials", "crd_alpha", "Alpha token", null,
                "{\"Id\":\"crd_alpha\",\"TenantId\":\"ten_default\",\"UserId\":\"usr_admin\",\"Name\":\"Alpha token\",\"BearerToken\":\"****WXYZ\",\"Active\":true}");
            credentials.ExtraRoutes.Add(new[] { "GET", "/api/v1/tenants", "{\"Objects\":[{\"Id\":\"ten_default\",\"Name\":\"Default Tenant\",\"Active\":true}],\"TotalRecords\":1}" });
            credentials.ExtraRoutes.Add(new[] { "GET", "/api/v1/users", "{\"Objects\":[{\"Id\":\"usr_admin\",\"TenantId\":\"ten_default\",\"Email\":\"admin@armada\",\"Active\":true}],\"TotalRecords\":1}" });
            specs.Add(credentials);

            TuiFlowSpec jobs = TuiFlowSpec.Create("jobs", "/jobs", "/api/v1/jobs", "job_1", "Alpha job", null,
                "{\"Id\":\"job_1\",\"Name\":\"Alpha job\",\"Kind\":\"Generic\",\"Status\":\"Succeeded\",\"Progress\":100," + Stamp + "}");
            jobs.NewKey = null;
            jobs.OpenKey = "j";
            specs.Add(jobs);

            TuiFlowSpec inbox = TuiFlowSpec.Create("needs_you", "/inbox", "/api/v1/inbox", "x", "Review: Alpha", "/missions/msn_alpha",
                "{\"Kind\":\"review\",\"Severity\":\"Warning\",\"Title\":\"Review: Alpha\",\"Detail\":\"Waiting 5m\",\"EntityType\":\"mission\",\"EntityId\":\"msn_alpha\",\"Href\":\"/missions/msn_alpha\"}");
            inbox.BareArray = true;
            inbox.DetailPath = "/api/v1/missions/msn_alpha";
            inbox.DetailJson = "{\"Id\":\"msn_alpha\",\"Title\":\"Alpha review mission\",\"Status\":\"Review\",\"VesselId\":\"vsl_1\"," + Stamp + "}";
            inbox.DetailText = "Alpha review mission";
            inbox.NewKey = null;
            inbox.RowMenuKey = null;
            specs.Add(inbox);

            return specs;
        }

        #endregion
    }
}
