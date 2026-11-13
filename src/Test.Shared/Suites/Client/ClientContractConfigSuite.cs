namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.IO;
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
    /// Armada.Client contract against a live server: configuration (personas, pipelines, prompt templates, skills,
    /// project and workflow profiles, memories, harbors, model endpoints, playbooks), administration (tenants, users,
    /// credentials, password change), and server settings, OpenAPI, backup download, rebuild status, and Mux endpoints.
    /// </summary>
    public sealed class ClientContractConfigSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Client.Contract.Config";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("voyage_approval_options", "SDK persists per-run approval choices on bare voyages and rejects invalid countdowns", async (c, fx) =>
            {
                Voyage voyage = (await c.CreateVoyageAsync(new VoyageCreateRequest { Title = "Approval contract " + ClientContract.Suffix(), ApprovalOptions = new RunApprovalOptions { ApproveAll = true, TimeoutSeconds = 45, TimeoutDecision = ApprovalTimeoutDecisionEnum.Approve } }))!;
                Voyage detail = (await c.GetVoyageDetailAsync(voyage.Id))!.Voyage!;
                AssertTrue(detail.ApprovalOptions!.ApproveAll);
                AssertEqual(45, detail.ApprovalOptions.TimeoutSeconds);
                AssertEqual(ApprovalTimeoutDecisionEnum.Approve, detail.ApprovalOptions.TimeoutDecision);
                await AssertThrowsAsync<ArmadaApiException>(() => c.CreateVoyageAsync(new VoyageCreateRequest { Title = "Bad countdown", ApprovalOptions = new RunApprovalOptions { TimeoutSeconds = 1 } }));
            }));

            cases.Add(Case("personas_pipelines_prompts", "Persona, pipeline, and prompt template create, get, update, reset, delete", async (c, fx) =>
            {
                string s = ClientContract.Suffix();
                Persona builtIn = (await c.ListPersonasAsync())!.Objects.First();
                Persona persona = (await c.CreatePersonaAsync(new Persona("ContractPersona" + s, builtIn.PromptTemplateName) { Description = "contract" }))!;
                AssertEqual("ContractPersona" + s, (await c.GetPersonaAsync(persona.Name))?.Name, "get persona");
                persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex, MinimumTier = CaptainTierEnum.Premium });
                persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ApiEndpoint, Model = "gpt-oss:120b", Endpoint = "mep_specific" });
                persona.PriorityAvailability = PersonaPriorityAvailabilityEnum.WaitForFirstPriority;
                persona.PriorityExhaustion = PersonaPriorityExhaustionEnum.Wait;
                await c.UpdatePersonaAsync(persona.Name, persona);
                Persona routedPersona = (await c.GetPersonaAsync(persona.Name))!;
                AssertEqual(2, routedPersona.CaptainPriorities.Count, "SDK round-trips ordered priorities");
                AssertEqual(AgentRuntimeEnum.Codex, routedPersona.CaptainPriorities[0].Runtime, "SDK preserves first runtime");
                AssertEqual("mep_specific", routedPersona.CaptainPriorities[1].Endpoint, "SDK preserves specific API endpoint");
                AssertEqual("gpt-oss:120b", routedPersona.CaptainPriorities[1].Model, "SDK preserves model");
                AssertEqual(PersonaPriorityAvailabilityEnum.WaitForFirstPriority, routedPersona.PriorityAvailability, "SDK preserves availability policy");
                persona.CaptainPriorities.Clear();
                AssertEqual(0, (await c.UpdatePersonaAsync(persona.Name, persona))!.CaptainPriorities.Count, "SDK clears hierarchy with empty list");
                persona.Description = "updated";
                AssertEqual("updated", (await c.UpdatePersonaAsync(persona.Name, persona))?.Description, "update persona");

                Pipeline pipeline = new Pipeline("ContractPipeline" + s);
                pipeline.Stages.Add(new PipelineStage(1, "Worker"));
                Pipeline createdPipeline = (await c.CreatePipelineAsync(pipeline))!;
                AssertEqual(1, (await c.GetPipelineAsync(createdPipeline.Name))?.Stages.Count ?? 0, "get pipeline");
                createdPipeline.Description = "updated";
                AssertEqual("updated", (await c.UpdatePipelineAsync(createdPipeline.Name, createdPipeline))?.Description, "update pipeline");
                await c.DeletePipelineAsync(createdPipeline.Name);
                await c.DeletePersonaAsync(persona.Name);
                await ClientContract.ExpectErrorAsync(() => c.GetPersonaAsync(persona.Name), "persona deleted", 404, 404);

                PromptTemplate builtInTemplate = (await c.ListPromptTemplatesAsync())!.Objects.First(t => t.IsBuiltIn);
                PromptTemplate? template = await c.GetPromptTemplateAsync(builtInTemplate.Name);
                AssertEqual(builtInTemplate.Name, template?.Name, "get template");
                PromptTemplateUpdateRequest update = new PromptTemplateUpdateRequest();
                update.Content = template!.Content + "\n<!-- contract -->";
                AssertContains("<!-- contract -->", (await c.UpdatePromptTemplateAsync(template.Name, update))?.Content ?? "", "update template");
                AssertFalse(((await c.ResetPromptTemplateAsync(template.Name))?.Content ?? "").Contains("<!-- contract -->"), "reset template");

                PromptTemplateCreateRequest create = new PromptTemplateCreateRequest();
                create.Name = "contract.template." + s;
                create.Category = builtInTemplate.Category;
                create.Content = "Hello {{Name}}";
                AssertEqual(create.Name, (await c.CreatePromptTemplateAsync(create))?.Name, "create template");
            }));

            cases.Add(Case("skills_memories_playbooks", "Skill, memory, and playbook create, get, list, update, delete", async (c, fx) =>
            {
                string s = ClientContract.Suffix();
                Skill skill = new Skill();
                skill.Name = "contract-skill-" + s;
                skill.Content = "# Skill";
                Skill createdSkill = (await c.CreateSkillAsync(skill))!;
                AssertEqual(skill.Name, (await c.GetSkillAsync(createdSkill.Id))?.Name, "get skill");
                createdSkill.Description = "updated";
                AssertEqual("updated", (await c.UpdateSkillAsync(createdSkill.Id, createdSkill))?.Description, "update skill");
                await c.DeleteSkillAsync(createdSkill.Id);

                Memory memory = new Memory();
                memory.Content = "Contract memory " + s;
                memory.Topic = "contract";
                Memory createdMemory = (await c.CreateMemoryAsync(memory))!;
                AssertEqual(memory.Content, (await c.GetMemoryAsync(createdMemory.Id))?.Content, "get memory");
                createdMemory.Summary = "updated";
                AssertEqual("updated", (await c.UpdateMemoryAsync(createdMemory.Id, createdMemory))?.Summary, "update memory");
                MemoryEnumerateRequest memoryQuery = new MemoryEnumerateRequest();
                memoryQuery.Search = "Contract memory " + s;
                memoryQuery.SortBy = MemorySortEnum.Updated;
                memoryQuery.PageSize = 5;
                EnumerationResult<Memory> enumerated = (await c.EnumerateMemoriesAsync(memoryQuery))!;
                AssertEqual(1L, enumerated.TotalRecords, "enumerate memories total");
                AssertEqual(createdMemory.Id, enumerated.Objects.Single().Id, "enumerate memories match");
                AssertNotNull(await c.EnumerateMemoriesAsync(), "enumerate memories default");
                await c.DeleteMemoryAsync(createdMemory.Id);

                Playbook playbook = (await c.CreatePlaybookAsync(new Playbook { FileName = "contract-" + s + ".md", Content = "# Steps" }))!;
                AssertEqual(playbook.FileName, (await c.GetPlaybookAsync(playbook.Id))?.FileName, "get playbook");
                AssertTrue((await c.ListPlaybooksAsync(new ArmadaPageQuery(1, 100)))!.Objects.Any(p => p.Id == playbook.Id), "list playbooks");
                await c.DeletePlaybookAsync(playbook.Id);
            }));

            cases.Add(Case("project_and_workflow_profiles", "Project and workflow profiles: CRUD, validate, resolve, preview", async (c, fx) =>
            {
                string s = ClientContract.Suffix();
                VesselSetup setup = await LiveServerSetup.CreateVesselAsync(c, "profiles");

                ProjectProfile project = new ProjectProfile();
                project.Name = "contract-project-" + s;
                project.Scope = ProjectProfileScopeEnum.Vessel;
                project.VesselId = setup.Vessel.Id;
                AssertTrue((await c.ValidateProjectProfileAsync(project))?.IsValid == true, "validate project profile");
                ProjectProfile createdProject = (await c.CreateProjectProfileAsync(project))!;
                AssertEqual(project.Name, (await c.GetProjectProfileAsync(createdProject.Id))?.Name, "get project profile");
                createdProject.Description = "updated";
                AssertEqual("updated", (await c.UpdateProjectProfileAsync(createdProject.Id, createdProject))?.Description, "update project profile");
                AssertNotNull(await c.ResolveProjectProfileForVesselAsync(setup.Vessel.Id, createdProject.Id), "resolve project profile");
                PersonaPromptPreview? preview = await c.PreviewPersonaPromptAsync(createdProject.Id, "Worker");
                AssertEqual("Worker", preview?.PersonaName, "persona preview");
                await c.DeleteProjectProfileAsync(createdProject.Id);

                WorkflowProfile workflow = new WorkflowProfile();
                workflow.Name = "contract-workflow-" + s;
                workflow.Scope = WorkflowProfileScopeEnum.Vessel;
                workflow.VesselId = setup.Vessel.Id;
                workflow.BuildCommand = "echo build";
                AssertTrue((await c.ValidateWorkflowProfileAsync(workflow))?.IsValid == true, "validate workflow profile");
                WorkflowProfile createdWorkflow = (await c.CreateWorkflowProfileAsync(workflow))!;
                AssertEqual(workflow.Name, (await c.GetWorkflowProfileAsync(createdWorkflow.Id))?.Name, "get workflow profile");
                createdWorkflow.Description = "updated";
                AssertEqual("updated", (await c.UpdateWorkflowProfileAsync(createdWorkflow.Id, createdWorkflow))?.Description, "update workflow profile");
                AssertEqual(createdWorkflow.Id, (await c.PreviewWorkflowProfileForVesselAsync(setup.Vessel.Id, createdWorkflow.Id))?.ResolvedProfile?.Id, "preview workflow profile");
                AssertEqual(createdWorkflow.Id, (await c.ResolveWorkflowProfileAsync(setup.Vessel.Id, createdWorkflow.Id))?.Id, "resolve workflow profile");
                EnumerationResult<WorkflowProfile>? enumeratedWorkflows = await c.EnumerateWorkflowProfilesAsync(new WorkflowProfileQuery { VesselId = setup.Vessel.Id, Search = workflow.Name });
                AssertEqual(createdWorkflow.Id, enumeratedWorkflows?.Objects.Single().Id, "enumerate workflow profiles filters by vessel and search");
                await c.DeleteWorkflowProfileAsync(createdWorkflow.Id);
                AssertEqual(0, (await c.EnumerateWorkflowProfilesAsync(new WorkflowProfileQuery { VesselId = setup.Vessel.Id, Search = workflow.Name }))?.Objects.Count, "deleted workflow profile no longer enumerated");

                WorkflowProfile batchWorkflow = new WorkflowProfile();
                batchWorkflow.Name = "contract-workflow-batch-" + s;
                batchWorkflow.BuildCommand = "echo build";
                WorkflowProfile createdBatchWorkflow = (await c.CreateWorkflowProfileAsync(batchWorkflow))!;
                BatchDeleteResult? batchDeleted = await c.DeleteWorkflowProfilesBatchAsync(new List<string> { createdBatchWorkflow.Id, "wfp_missing_" + s });
                AssertEqual(1, batchDeleted?.Deleted, "batch delete workflow profiles deletes the existing id");
                AssertEqual(1, batchDeleted?.Skipped.Count, "batch delete workflow profiles skips the missing id");
                await AssertThrowsAsync<ArmadaApiException>(() => c.GetWorkflowProfileAsync(createdBatchWorkflow.Id), "batch-deleted workflow profile is gone");
            }));

            cases.Add(Case("harbors_and_endpoints", "Harbor CRUD, enable and disable, metrics, and jobs; model endpoint CRUD, validate, and health sweep", async (c, fx) =>
            {
                string s = ClientContract.Suffix();
                Harbor harbor = (await c.CreateHarborAsync(new Harbor { Name = "contract-harbor-" + s }))!;
                AssertEqual(harbor.Name, (await c.GetHarborAsync(harbor.Id))?.Name, "get harbor");
                AssertFalse((await c.DisableHarborAsync(harbor.Id))?.Enabled ?? true, "disabled");
                AssertTrue((await c.EnableHarborAsync(harbor.Id))?.Enabled ?? false, "enabled");
                harbor.MaxConcurrentJobs = 3;
                AssertEqual(3, (await c.UpdateHarborAsync(harbor.Id, harbor))?.MaxConcurrentJobs ?? 0, "update harbor");
                HarborMetrics? metrics = await c.GetHarborMetricsAsync(harbor.Id, "1h");
                AssertEqual(harbor.Id, metrics?.HarborId, "harbor metrics");
                AssertEqual(60, metrics?.BucketCount ?? 0, "1h in 1-minute buckets");
                AssertEqual(3, metrics?.Slots.MaxConcurrentJobs ?? 0, "capacity in the slot series");
                AssertEqual("24h", (await c.GetHarborMetricsAsync(harbor.Id))?.Range, "24h by default");
                EnumerationResult<HarborJobRecord>? jobs = await c.ListHarborJobsAsync(harbor.Id, true, new ArmadaPageQuery { PageNumber = 1, PageSize = 5 });
                AssertEqual(0L, jobs?.TotalRecords ?? -1, "a new Harbor has no recorded jobs");
                AssertEqual(5, jobs?.PageSize ?? 0, "the page size is echoed");
                await AssertThrowsAsync<ArmadaApiException>(() => c.StopHarborJobAsync(harbor.Id, "hjob_missing"));
                await c.DeleteHarborAsync(harbor.Id);

                ModelEndpoint endpoint = new ModelEndpoint();
                endpoint.Name = "contract-endpoint-" + s;
                endpoint.Kind = ModelEndpointKindEnum.Inference;
                endpoint.Provider = ModelProviderEnum.OpenAICompatible;
                endpoint.BaseUrl = "http://127.0.0.1:9";
                endpoint.Model = "contract-model";
                endpoint.TimeoutMs = 2000;
                endpoint.InferenceOptions = new InferenceOptions { ToolsEnabled = false, McpToolsEnabled = false, ReadOnlyTools = true, RequireHarbor = true, MaxIterations = 7, MaxTokens = 123, Temperature = 0.3, TopP = 0.8, AllowedTools = new List<string> { "read_file" } };
                ModelEndpoint createdEndpoint = (await c.CreateModelEndpointAsync(endpoint, "sk-contract"))!;
                AssertEqual(false, createdEndpoint.InferenceOptions.ToolsEnabled, "SDK/REST policy round-trip");
                AssertEqual(7, createdEndpoint.InferenceOptions.MaxIterations, "SDK/REST generation round-trip");
                AssertEqual("read_file", createdEndpoint.InferenceOptions.AllowedTools![0], "SDK/REST exact tool allowlist");
                AssertTrue(createdEndpoint.InferenceOptions.RequireHarbor, "SDK/REST Harbor requirement");
                AssertTrue(createdEndpoint.HasApiKey, "api key stored");
                AssertNull(createdEndpoint.ApiKey, "api key never returned");
                AssertEqual(endpoint.Name, (await c.GetModelEndpointAsync(createdEndpoint.Id))?.Name, "get endpoint");
                ModelEndpoint omittedPolicy = new ModelEndpoint { Name = endpoint.Name, Kind = endpoint.Kind, Provider = endpoint.Provider, BaseUrl = endpoint.BaseUrl, Model = endpoint.Model };
                ModelEndpoint preservedPolicy = (await c.UpdateModelEndpointAsync(createdEndpoint.Id, omittedPolicy))!;
                AssertEqual(7, preservedPolicy.InferenceOptions.MaxIterations, "SDK omitted policy is preserved");
                AssertEqual(false, preservedPolicy.InferenceOptions.ToolsEnabled, "SDK omitted policy cannot re-enable tools");
                createdEndpoint.Model = "contract-model-2";
                AssertEqual("contract-model-2", (await c.UpdateModelEndpointAsync(createdEndpoint.Id, createdEndpoint))?.Model, "update endpoint");
                ModelEndpoint copyInput = new ModelEndpoint { Name = "copied-endpoint-" + s, Kind = endpoint.Kind, Provider = endpoint.Provider, BaseUrl = endpoint.BaseUrl, Model = "copied-model" };
                ModelEndpoint copy = (await c.DuplicateModelEndpointAsync(createdEndpoint.Id, copyInput))!;
                AssertTrue(copy.Id != createdEndpoint.Id, "SDK duplicate creates a fresh identity");
                AssertTrue(copy.HasApiKey, "SDK duplicate inherits credentials"); AssertNull(copy.ApiKey, "SDK duplicate response hides credentials");
                AssertEqual(7, copy.InferenceOptions.MaxIterations, "SDK duplicate inherits omitted policy");
                AssertEqual("copied-model", copy.Model, "SDK duplicate saves edited model");
                await c.DeleteModelEndpointAsync(copy.Id);
                ModelEndpointProbeResult? probe = await c.ValidateModelEndpointAsync(createdEndpoint.Id);
                AssertFalse(probe?.Success ?? true, "nothing listens on port 9");
                AssertNotNull(await c.HealthCheckModelEndpointsAsync(), "health sweep");
                await c.DeleteModelEndpointAsync(createdEndpoint.Id);
            }));

            cases.Add(Case("tenants_users_credentials_password", "Tenant, user, and credential CRUD; a new user changes its password", async (c, fx) =>
            {
                string s = ClientContract.Suffix();
                TenantMetadata tenant = (await c.CreateTenantAsync(new TenantMetadata("contract-tenant-" + s)))!;
                tenant.Name = "contract-tenant-renamed-" + s;
                AssertEqual(tenant.Name, (await c.UpdateTenantAsync(tenant.Id, tenant))?.Name, "update tenant");

                UserUpsertRequest user = new UserUpsertRequest();
                user.TenantId = "default";
                user.Email = "contract-" + s + "@armada.test";
                user.Password = "Contract-Pass-1";
                user.Active = true;
                UserMaster createdUser = (await c.CreateUserAsync(user))!;
                user.FirstName = "Contract";
                AssertEqual("Contract", (await c.UpdateUserAsync(createdUser.Id, user))?.FirstName, "update user");

                using (ArmadaClient session = new ArmadaClient(fx.BaseUrl))
                {
                    AuthenticateResult? auth = await session.AuthenticateAsync(new AuthenticateRequest { Email = user.Email, Password = user.Password, TenantId = "default" });
                    AssertTrue(auth?.Success == true, "new user signs in");
                    session.SetToken(auth!.Token);
                    WhoAmIResult? changed = await session.ChangePasswordAsync(new PasswordChangeRequest { CurrentPassword = "Contract-Pass-1", NewPassword = "Contract-Pass-2" });
                    AssertEqual(user.Email, changed?.User?.Email, "password changed");
                }

                using (ArmadaClient session = new ArmadaClient(fx.BaseUrl))
                {
                    AuthenticateResult? again = await session.AuthenticateAsync(new AuthenticateRequest { Email = user.Email, Password = "Contract-Pass-2", TenantId = "default" });
                    AssertTrue(again?.Success == true, "the new password works");
                }

                Credential credential = new Credential("default", createdUser.Id);
                credential.Name = "contract-credential";
                Credential createdCredential = (await c.CreateCredentialAsync(credential))!;
                AssertFalse(String.IsNullOrEmpty(createdCredential.BearerToken), "token minted");
                createdCredential.Name = "renamed";
                AssertEqual("renamed", (await c.UpdateCredentialAsync(createdCredential.Id, createdCredential))?.Name, "update credential");
                await c.DeleteCredentialAsync(createdCredential.Id);
                await c.DeleteUserAsync(createdUser.Id);
                await c.DeleteTenantAsync(tenant.Id);
            }));

            cases.Add(Case("server_settings_and_tools", "Settings round trip, OpenAPI document, backup download, rebuild status, Mux endpoints, proxy calls against an Admiral", async (c, fx) =>
            {
                SettingsData settings = (await c.GetSettingsAsync())!;
                int original = settings.MaxCaptains ?? 0;
                settings.MaxCaptains = original + 1;
                AssertEqual(original + 1, (await c.UpdateSettingsAsync(settings))?.MaxCaptains ?? 0, "settings saved");
                settings.MaxCaptains = original;
                await c.UpdateSettingsAsync(settings);

                AssertEqual(LandingModeEnum.MergeAndPush, (await c.GetSettingsAsync())?.LandingMode, "the global landing mode defaults to MergeAndPush");
                SettingsData landing = new SettingsData();
                landing.LandingMode = LandingModeEnum.LocalMerge;
                AssertEqual(LandingModeEnum.LocalMerge, (await c.UpdateSettingsAsync(landing))?.LandingMode, "PUT returns the new global landing mode");
                AssertEqual(LandingModeEnum.LocalMerge, (await c.GetSettingsAsync())?.LandingMode, "GET reads it back");
                AssertEqual(original, (await c.GetSettingsAsync())?.MaxCaptains ?? -1, "a landing-only PUT leaves other settings alone");
                landing.LandingMode = LandingModeEnum.MergeAndPush;
                AssertEqual(LandingModeEnum.MergeAndPush, (await c.UpdateSettingsAsync(landing))?.LandingMode, "restored");

                ArmadaRawJson? openApi = await c.GetOpenApiDocumentAsync();
                AssertNotNull(openApi, "OpenAPI document");
                ClientOpenApiDocument openApiDocument = JsonHelper.Deserialize<ClientOpenApiDocument>(openApi!.Json);
                AssertTrue(openApiDocument.Paths != null && openApiDocument.Paths.TryGetValue("/api/v1/events/{id}", out ClientOpenApiPathItem? eventPath) && eventPath.Get != null, "OpenAPI lists GET /api/v1/events/{id}");
                BackupFile backup = await c.DownloadBackupAsync();
                AssertTrue(backup.Content.Length > 0, "backup bytes");
                AssertTrue(backup.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase), "backup file name: " + backup.FileName);
                AssertNotNull(await c.GetRebuildStatusAsync(), "rebuild status");

                string muxDir = TestTemp.NewDirectory("mux-config");
                try
                {
                    MuxEndpointPage? mux = await c.ListMuxEndpointsAsync(muxDir);
                    AssertNotNull(mux, "mux list reply");
                    MuxEndpointPage? muxPage = await c.EnumerateMuxEndpointsAsync(muxDir, 1, 5);
                    AssertNotNull(await c.ListAllMuxEndpointsAsync(muxDir), "mux picker list");
                    AssertNotNull(muxPage, "mux page reply");
                }
                catch (ArmadaApiException ex)
                {
                    AssertTrue(ex.StatusCode >= 400, "mux unavailable maps to an error reply: " + ex.StatusCode);
                }

                await ClientContract.ExpectErrorAsync(() => c.GetMuxEndpointAsync("contract-missing", muxDir), "unknown mux endpoint");

                AssertNull(await c.GetProxySessionContextAsync(), "not behind Armada.Proxy");
                await c.LogoutProxyAsync();
                await ClientContract.ExpectErrorAsync(() => c.ClearProxySessionInstanceAsync(), "proxy route absent on an Admiral", 404, 404);
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client contract: configuration and administration (live server)", cases: cases);
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
