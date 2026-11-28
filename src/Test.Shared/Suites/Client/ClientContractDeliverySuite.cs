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

                DeploymentEnvironment? environment = await c.Environments.GetAsync(environmentId);
                AssertEqual("production", environment?.Name, "get environment");
                DeploymentEnvironmentUpsertRequest envUpdate = new DeploymentEnvironmentUpsertRequest();
                envUpdate.Description = "updated";
                AssertEqual("updated", (await c.Environments.UpdateAsync(environmentId, envUpdate))?.Description, "update environment");
                AssertTrue((await c.Environments.ListAsync(new DeploymentEnvironmentQuery { VesselId = setup.Vessel.Id }))!.Objects.Any(e => e.Id == environmentId), "list environments by vessel");
                AssertTrue((await c.Environments.EnumerateAsync(new DeploymentEnvironmentQuery { VesselId = setup.Vessel.Id }))!.Objects.Any(e => e.Id == environmentId), "enumerate environments");

                Deployment approved = (await c.Deployments.ApproveAsync(pending.Id, "contract approval"))!;
                AssertEqual(DeploymentStatusEnum.Succeeded, approved.Status, "approved deployment ran");
                AssertEqual("contract approval", approved.ApprovalComment, "approval comment");
                AssertEqual(DeploymentStatusEnum.Succeeded, (await c.Deployments.VerifyAsync(pending.Id))?.Status, "verify");
                AssertEqual(DeploymentStatusEnum.RolledBack, (await c.Deployments.RollbackAsync(pending.Id))?.Status, "rollback");

                DeploymentUpsertRequest second = new DeploymentUpsertRequest();
                second.VesselId = setup.Vessel.Id;
                second.WorkflowProfileId = approved.WorkflowProfileId;
                second.EnvironmentId = environmentId;
                second.Title = "Contract deploy later";
                second.AutoExecute = true;
                Deployment toDeny = (await c.Deployments.CreateAsync(second))!;
                AssertEqual(DeploymentStatusEnum.Denied, (await c.Deployments.DenyAsync(toDeny.Id, "not now"))?.Status, "deny");
                DeploymentUpsertRequest rename = new DeploymentUpsertRequest();
                rename.Notes = "contract notes";
                AssertEqual("contract notes", (await c.Deployments.UpdateAsync(toDeny.Id, rename))?.Notes, "update deployment");
                AssertEqual("Contract deploy later", (await c.Deployments.GetAsync(toDeny.Id))?.Title, "get deployment");
                AssertTrue((await c.Deployments.EnumerateAsync(new DeploymentQuery { VesselId = setup.Vessel.Id }))!.Objects.Count >= 2, "enumerate deployments");
                await c.Deployments.DeleteAsync(toDeny.Id);
                await ClientContract.ExpectErrorAsync(() => c.Deployments.GetAsync(toDeny.Id), "deployment deleted", 404, 404);

                DeploymentEnvironmentUpsertRequest scratch = new DeploymentEnvironmentUpsertRequest();
                scratch.VesselId = setup.Vessel.Id;
                scratch.Name = "scratch-" + ClientContract.Suffix();
                scratch.Kind = EnvironmentKindEnum.Development;
                DeploymentEnvironment scratchEnv = (await c.Environments.CreateAsync(scratch))!;
                await c.Environments.DeleteAsync(scratchEnv.Id);
            }));

            cases.Add(Case("releases_incidents", "Release CRUD, refresh, enumerate, GitHub pull requests; incident CRUD and enumerate", async (c, fx) =>
            {
                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "release");
                ReleaseUpsertRequest release = new ReleaseUpsertRequest();
                release.VesselId = setup.Vessel.Id;
                release.Title = "Contract release";
                release.Version = "1.2.3";
                Release created = (await c.Releases.CreateAsync(release))!;
                AssertEqual("1.2.3", (await c.Releases.GetAsync(created.Id))?.Version, "get release");
                release.Notes = "contract notes";
                AssertEqual("contract notes", (await c.Releases.UpdateAsync(created.Id, release))?.Notes, "update release");
                AssertEqual(created.Id, (await c.Releases.RefreshAsync(created.Id))?.Id, "refresh release");
                AssertTrue((await c.Releases.EnumerateAsync(new ReleaseQuery { VesselId = setup.Vessel.Id }))!.Objects.Any(r => r.Id == created.Id), "enumerate releases");
                try
                {
                    EnumerationResult<GitHubPullRequestDetail>? prs = await c.Releases.GetGitHubPullRequestsAsync(created.Id, new EnumerationQuery { PageSize = 5 });
                    AssertEqual(0, prs?.Objects.Count ?? 0, "no pull requests for a local vessel");
                    AssertEqual(0L, prs?.TotalRecords ?? 0, "exact total");
                    AssertEqual(5, prs?.PageSize ?? 0, "requested page size");
                }
                catch (ArmadaApiException ex)
                {
                    AssertTrue(ex.StatusCode >= 400 && ex.StatusCode < 600, "GitHub unavailable maps to an error reply: " + ex.StatusCode);
                }

                await c.Releases.DeleteAsync(created.Id);

                IncidentUpsertRequest incident = new IncidentUpsertRequest();
                incident.Title = "Contract incident";
                incident.VesselId = setup.Vessel.Id;
                incident.Severity = IncidentSeverityEnum.High;
                Incident createdIncident = (await c.Incidents.CreateAsync(incident))!;
                AssertEqual("Contract incident", (await c.Incidents.GetAsync(createdIncident.Id))?.Title, "get incident");
                incident.RootCause = "contract";
                AssertEqual("contract", (await c.Incidents.UpdateAsync(createdIncident.Id, incident))?.RootCause, "update incident");
                AssertTrue((await c.Incidents.EnumerateAsync(new IncidentQuery { VesselId = setup.Vessel.Id }))!.Objects.Any(i => i.Id == createdIncident.Id), "enumerate incidents");
                await c.Incidents.DeleteAsync(createdIncident.Id);
            }));

            cases.Add(Case("runbooks_and_executions", "Runbook CRUD and enumerate; execution start, get, update, list, enumerate, delete", async (c, fx) =>
            {
                string s = ClientContract.Suffix();
                RunbookUpsertRequest runbook = new RunbookUpsertRequest();
                runbook.FileName = "contract-runbook-" + s + ".md";
                runbook.Title = "Contract runbook";
                runbook.Steps = new List<RunbookStep> { new RunbookStep { Id = "step-1", Title = "Check", Instructions = "Look." } };
                Runbook created = (await c.Runbooks.CreateAsync(runbook))!;
                AssertEqual("Contract runbook", (await c.Runbooks.GetAsync(created.Id))?.Title, "get runbook");
                runbook.Description = "updated";
                AssertEqual("updated", (await c.Runbooks.UpdateAsync(created.Id, runbook))?.Description, "update runbook");
                AssertTrue((await c.Runbooks.EnumerateAsync(new RunbookQuery { Search = "Contract runbook" }))!.Objects.Any(r => r.Id == created.Id), "enumerate runbooks");

                RunbookExecution execution = (await c.Runbooks.StartExecutionAsync(created.Id, new RunbookExecutionStartRequest { Title = "Contract execution" }))!;
                AssertEqual(RunbookExecutionStatusEnum.Running, execution.Status, "started");
                AssertEqual(execution.Id, (await c.Runbooks.GetExecutionAsync(execution.Id))?.Id, "get execution");
                RunbookExecutionUpdateRequest update = new RunbookExecutionUpdateRequest();
                update.CompletedStepIds = new List<string> { "step-1" };
                update.Status = RunbookExecutionStatusEnum.Completed;
                AssertEqual(RunbookExecutionStatusEnum.Completed, (await c.Runbooks.UpdateExecutionAsync(execution.Id, update))?.Status, "complete execution");
                AssertTrue((await c.Runbooks.ListExecutionsAsync(new RunbookExecutionQuery { RunbookId = created.Id }))!.Objects.Any(e => e.Id == execution.Id), "list executions");
                AssertTrue((await c.Runbooks.EnumerateExecutionsAsync(new RunbookExecutionQuery { RunbookId = created.Id }))!.Objects.Any(e => e.Id == execution.Id), "enumerate executions");
                await c.Runbooks.DeleteExecutionAsync(execution.Id);
                await c.Runbooks.DeleteAsync(created.Id);
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
                WorkflowProfile createdWorkflow = (await c.WorkflowProfiles.CreateAsync(workflow))!;

                CheckRunRequest run = new CheckRunRequest();
                run.VesselId = setup.Vessel.Id;
                run.WorkflowProfileId = createdWorkflow.Id;
                run.Type = CheckRunTypeEnum.Build;
                CheckRun started = (await c.CheckRuns.RunAsync(run))!;
                CheckRun? finished = null;
                AssertTrue(await LiveServerSetup.WaitUntilAsync(async () =>
                {
                    finished = await c.CheckRuns.GetAsync(started.Id);
                    return finished != null && finished.Status != CheckRunStatusEnum.Pending && finished.Status != CheckRunStatusEnum.Running;
                }, 60000), "check run finished");
                AssertEqual(CheckRunStatusEnum.Passed, finished!.Status, "echo passes: " + finished.Output);
                CheckRun retried = (await c.CheckRuns.RetryAsync(started.Id))!;
                AssertNotEqual(started.Id, retried.Id, "retry creates a new run");

                CheckRunImportRequest import = new CheckRunImportRequest();
                import.VesselId = setup.Vessel.Id;
                import.Type = CheckRunTypeEnum.UnitTest;
                import.Status = CheckRunStatusEnum.Passed;
                import.ProviderName = "contract-ci";
                import.ExternalId = "run-" + ClientContract.Suffix();
                CheckRun imported = (await c.CheckRuns.ImportAsync(import))!;
                AssertEqual("contract-ci", imported.ProviderName, "imported");
                await c.CheckRuns.DeleteAsync(imported.Id);

                GitHubActionsSyncRequest sync = new GitHubActionsSyncRequest();
                sync.VesselId = setup.Vessel.Id;
                await ClientContract.ExpectErrorAsync(() => c.Deployments.SyncGitHubActionsAsync(sync), "a local vessel has no GitHub repository");
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
