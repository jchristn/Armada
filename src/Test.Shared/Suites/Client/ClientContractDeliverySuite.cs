namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Armada.Client contract against a live server: delivery (environments, deployments with approval, verify, and
    /// rollback, releases, incidents, runbooks and their executions, check runs, GitHub Actions sync).
    /// </summary>
    public sealed class ClientContractDeliverySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Client.Contract.Delivery";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("environments_and_deployments", "Environment CRUD and enumerate; deployment approve, deny, verify, rollback, update, enumerate, delete", async (c, fx) =>
            {
                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "delivery");
                Deployment pending = await LiveServerSetup.CreatePendingDeploymentAsync(c, setup.Vessel.Id, "Contract deploy");
                AssertEqual(DeploymentStatusEnum.PendingApproval, pending.Status, "pending approval");
                string environmentId = pending.EnvironmentId!;

                DeploymentEnvironment? environment = await c.GetEnvironmentAsync(environmentId);
                AssertEqual("production", environment?.Name, "get environment");
                DeploymentEnvironmentUpsertRequest envUpdate = new DeploymentEnvironmentUpsertRequest();
                envUpdate.Description = "updated";
                AssertEqual("updated", (await c.UpdateEnvironmentAsync(environmentId, envUpdate))?.Description, "update environment");
                AssertTrue((await c.ListEnvironmentsAsync(new DeploymentEnvironmentQuery { VesselId = setup.Vessel.Id }))!.Objects.Any(e => e.Id == environmentId), "list environments by vessel");
                AssertTrue((await c.EnumerateEnvironmentsAsync(new DeploymentEnvironmentQuery { VesselId = setup.Vessel.Id }))!.Objects.Any(e => e.Id == environmentId), "enumerate environments");

                Deployment approved = (await c.ApproveDeploymentAsync(pending.Id, "contract approval"))!;
                AssertEqual(DeploymentStatusEnum.Succeeded, approved.Status, "approved deployment ran");
                AssertEqual("contract approval", approved.ApprovalComment, "approval comment");
                AssertEqual(DeploymentStatusEnum.Succeeded, (await c.VerifyDeploymentAsync(pending.Id))?.Status, "verify");
                AssertEqual(DeploymentStatusEnum.RolledBack, (await c.RollbackDeploymentAsync(pending.Id))?.Status, "rollback");

                DeploymentUpsertRequest second = new DeploymentUpsertRequest();
                second.VesselId = setup.Vessel.Id;
                second.WorkflowProfileId = approved.WorkflowProfileId;
                second.EnvironmentId = environmentId;
                second.Title = "Contract deploy later";
                second.AutoExecute = true;
                Deployment toDeny = (await c.CreateDeploymentAsync(second))!;
                AssertEqual(DeploymentStatusEnum.Denied, (await c.DenyDeploymentAsync(toDeny.Id, "not now"))?.Status, "deny");
                DeploymentUpsertRequest rename = new DeploymentUpsertRequest();
                rename.Notes = "contract notes";
                AssertEqual("contract notes", (await c.UpdateDeploymentAsync(toDeny.Id, rename))?.Notes, "update deployment");
                AssertEqual("Contract deploy later", (await c.GetDeploymentAsync(toDeny.Id))?.Title, "get deployment");
                AssertTrue((await c.EnumerateDeploymentsAsync(new DeploymentQuery { VesselId = setup.Vessel.Id }))!.Objects.Count >= 2, "enumerate deployments");
                await c.DeleteDeploymentAsync(toDeny.Id);
                await ClientContract.ExpectErrorAsync(() => c.GetDeploymentAsync(toDeny.Id), "deployment deleted", 404, 404);

                DeploymentEnvironmentUpsertRequest scratch = new DeploymentEnvironmentUpsertRequest();
                scratch.VesselId = setup.Vessel.Id;
                scratch.Name = "scratch-" + ClientContract.Suffix();
                scratch.Kind = EnvironmentKindEnum.Development;
                DeploymentEnvironment scratchEnv = (await c.CreateEnvironmentAsync(scratch))!;
                await c.DeleteEnvironmentAsync(scratchEnv.Id);
            }));

            cases.Add(Case("releases_incidents", "Release CRUD, refresh, enumerate, GitHub pull requests; incident CRUD and enumerate", async (c, fx) =>
            {
                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "release");
                ReleaseUpsertRequest release = new ReleaseUpsertRequest();
                release.VesselId = setup.Vessel.Id;
                release.Title = "Contract release";
                release.Version = "1.2.3";
                Release created = (await c.CreateReleaseAsync(release))!;
                AssertEqual("1.2.3", (await c.GetReleaseAsync(created.Id))?.Version, "get release");
                release.Notes = "contract notes";
                AssertEqual("contract notes", (await c.UpdateReleaseAsync(created.Id, release))?.Notes, "update release");
                AssertEqual(created.Id, (await c.RefreshReleaseAsync(created.Id))?.Id, "refresh release");
                AssertTrue((await c.EnumerateReleasesAsync(new ReleaseQuery { VesselId = setup.Vessel.Id }))!.Objects.Any(r => r.Id == created.Id), "enumerate releases");
                try
                {
                    List<GitHubPullRequestDetail>? prs = await c.GetReleaseGitHubPullRequestsAsync(created.Id);
                    AssertEqual(0, prs?.Count ?? 0, "no pull requests for a local vessel");
                }
                catch (ArmadaApiException ex)
                {
                    AssertTrue(ex.StatusCode >= 400 && ex.StatusCode < 600, "GitHub unavailable maps to an error reply: " + ex.StatusCode);
                }

                await c.DeleteReleaseAsync(created.Id);

                IncidentUpsertRequest incident = new IncidentUpsertRequest();
                incident.Title = "Contract incident";
                incident.VesselId = setup.Vessel.Id;
                incident.Severity = IncidentSeverityEnum.High;
                Incident createdIncident = (await c.CreateIncidentAsync(incident))!;
                AssertEqual("Contract incident", (await c.GetIncidentAsync(createdIncident.Id))?.Title, "get incident");
                incident.RootCause = "contract";
                AssertEqual("contract", (await c.UpdateIncidentAsync(createdIncident.Id, incident))?.RootCause, "update incident");
                AssertTrue((await c.EnumerateIncidentsAsync(new IncidentQuery { VesselId = setup.Vessel.Id }))!.Objects.Any(i => i.Id == createdIncident.Id), "enumerate incidents");
                await c.DeleteIncidentAsync(createdIncident.Id);
            }));

            cases.Add(Case("runbooks_and_executions", "Runbook CRUD and enumerate; execution start, get, update, list, enumerate, delete", async (c, fx) =>
            {
                string s = ClientContract.Suffix();
                RunbookUpsertRequest runbook = new RunbookUpsertRequest();
                runbook.FileName = "contract-runbook-" + s + ".md";
                runbook.Title = "Contract runbook";
                runbook.Steps = new List<RunbookStep> { new RunbookStep { Id = "step-1", Title = "Check", Instructions = "Look." } };
                Runbook created = (await c.CreateRunbookAsync(runbook))!;
                AssertEqual("Contract runbook", (await c.GetRunbookAsync(created.Id))?.Title, "get runbook");
                runbook.Description = "updated";
                AssertEqual("updated", (await c.UpdateRunbookAsync(created.Id, runbook))?.Description, "update runbook");
                AssertTrue((await c.EnumerateRunbooksAsync(new RunbookQuery { Search = "Contract runbook" }))!.Objects.Any(r => r.Id == created.Id), "enumerate runbooks");

                RunbookExecution execution = (await c.StartRunbookExecutionAsync(created.Id, new RunbookExecutionStartRequest { Title = "Contract execution" }))!;
                AssertEqual(RunbookExecutionStatusEnum.Running, execution.Status, "started");
                AssertEqual(execution.Id, (await c.GetRunbookExecutionAsync(execution.Id))?.Id, "get execution");
                RunbookExecutionUpdateRequest update = new RunbookExecutionUpdateRequest();
                update.CompletedStepIds = new List<string> { "step-1" };
                update.Status = RunbookExecutionStatusEnum.Completed;
                AssertEqual(RunbookExecutionStatusEnum.Completed, (await c.UpdateRunbookExecutionAsync(execution.Id, update))?.Status, "complete execution");
                AssertTrue((await c.ListRunbookExecutionsAsync(new RunbookExecutionQuery { RunbookId = created.Id }))!.Objects.Any(e => e.Id == execution.Id), "list executions");
                AssertTrue((await c.EnumerateRunbookExecutionsAsync(new RunbookExecutionQuery { RunbookId = created.Id }))!.Objects.Any(e => e.Id == execution.Id), "enumerate executions");
                await c.DeleteRunbookExecutionAsync(execution.Id);
                await c.DeleteRunbookAsync(created.Id);
            }));

            cases.Add(Case("check_runs", "Check run run, get, retry, import, delete; GitHub Actions sync without GitHub is an error reply", async (c, fx) =>
            {
                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "checks");
                WorkflowProfile workflow = new WorkflowProfile();
                workflow.Name = "contract-checks-" + ClientContract.Suffix();
                workflow.Scope = WorkflowProfileScopeEnum.Vessel;
                workflow.VesselId = setup.Vessel.Id;
                workflow.IsDefault = true;
                workflow.BuildCommand = "echo contract-build";
                WorkflowProfile createdWorkflow = (await c.CreateWorkflowProfileAsync(workflow))!;

                CheckRunRequest run = new CheckRunRequest();
                run.VesselId = setup.Vessel.Id;
                run.WorkflowProfileId = createdWorkflow.Id;
                run.Type = CheckRunTypeEnum.Build;
                CheckRun started = (await c.RunCheckAsync(run))!;
                CheckRun? finished = null;
                AssertTrue(await LiveServerSetup.WaitUntilAsync(async () =>
                {
                    finished = await c.GetCheckRunAsync(started.Id);
                    return finished != null && finished.Status != CheckRunStatusEnum.Pending && finished.Status != CheckRunStatusEnum.Running;
                }, 60000), "check run finished");
                AssertEqual(CheckRunStatusEnum.Passed, finished!.Status, "echo passes: " + finished.Output);
                CheckRun retried = (await c.RetryCheckRunAsync(started.Id))!;
                AssertNotEqual(started.Id, retried.Id, "retry creates a new run");

                CheckRunImportRequest import = new CheckRunImportRequest();
                import.VesselId = setup.Vessel.Id;
                import.Type = CheckRunTypeEnum.UnitTest;
                import.Status = CheckRunStatusEnum.Passed;
                import.ProviderName = "contract-ci";
                import.ExternalId = "run-" + ClientContract.Suffix();
                CheckRun imported = (await c.ImportCheckRunAsync(import))!;
                AssertEqual("contract-ci", imported.ProviderName, "imported");
                await c.DeleteCheckRunAsync(imported.Id);

                GitHubActionsSyncRequest sync = new GitHubActionsSyncRequest();
                sync.VesselId = setup.Vessel.Id;
                await ClientContract.ExpectErrorAsync(() => c.SyncGitHubActionsAsync(sync), "a local vessel has no GitHub repository");
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client contract: delivery (live server)", cases: cases);
        }

        #endregion

        #region Private-Methods

        private TestCaseDescriptor Case(string id, string name, Func<ArmadaClient, E2EServerFixture, Task> body)
        {
            return ClientContract.Case(Suite, this, id, name, body);
        }

        #endregion
    }
}
