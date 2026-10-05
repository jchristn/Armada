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

            cases.Add(Case("personas_pipelines_prompts", "Persona, pipeline, and prompt template create, get, update, reset, delete", async (c, fx) =>
            {
                string s = ClientContract.Suffix();
                Persona builtIn = (await c.ListPersonasAsync())!.Objects.First();
                Persona persona = (await c.CreatePersonaAsync(new Persona("ContractPersona" + s, builtIn.PromptTemplateName) { Description = "contract" }))!;
                AssertEqual("ContractPersona" + s, (await c.GetPersonaAsync(persona.Name))?.Name, "get persona");
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
                await c.DeleteWorkflowProfileAsync(createdWorkflow.Id);
            }));

            cases.Add(Case("harbors_and_endpoints", "Harbor CRUD, enable and disable; model endpoint CRUD, validate, and health sweep", async (c, fx) =>
            {
                string s = ClientContract.Suffix();
                Harbor harbor = (await c.CreateHarborAsync(new Harbor { Name = "contract-harbor-" + s }))!;
                AssertEqual(harbor.Name, (await c.GetHarborAsync(harbor.Id))?.Name, "get harbor");
                AssertFalse((await c.DisableHarborAsync(harbor.Id))?.Enabled ?? true, "disabled");
                AssertTrue((await c.EnableHarborAsync(harbor.Id))?.Enabled ?? false, "enabled");
                harbor.MaxConcurrentJobs = 3;
                AssertEqual(3, (await c.UpdateHarborAsync(harbor.Id, harbor))?.MaxConcurrentJobs ?? 0, "update harbor");
                await c.DeleteHarborAsync(harbor.Id);

                ModelEndpoint endpoint = new ModelEndpoint();
                endpoint.Name = "contract-endpoint-" + s;
                endpoint.Kind = ModelEndpointKindEnum.Inference;
                endpoint.Provider = ModelProviderEnum.OpenAICompatible;
                endpoint.BaseUrl = "http://127.0.0.1:9";
                endpoint.Model = "contract-model";
                endpoint.TimeoutMs = 2000;
                ModelEndpoint createdEndpoint = (await c.CreateModelEndpointAsync(endpoint, "sk-contract"))!;
                AssertTrue(createdEndpoint.HasApiKey, "api key stored");
                AssertNull(createdEndpoint.ApiKey, "api key never returned");
                AssertEqual(endpoint.Name, (await c.GetModelEndpointAsync(createdEndpoint.Id))?.Name, "get endpoint");
                createdEndpoint.Model = "contract-model-2";
                AssertEqual("contract-model-2", (await c.UpdateModelEndpointAsync(createdEndpoint.Id, createdEndpoint))?.Model, "update endpoint");
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

                ArmadaRawJson? openApi = await c.GetOpenApiDocumentAsync();
                AssertContains("\"/api/v1/events/{id}\"", openApi?.Json ?? "", "OpenAPI lists the routes");
                BackupFile backup = await c.DownloadBackupAsync();
                AssertTrue(backup.Content.Length > 0, "backup bytes");
                AssertTrue(backup.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase), "backup file name: " + backup.FileName);
                AssertNotNull(await c.GetRebuildStatusAsync(), "rebuild status");

                string muxDir = TestTemp.NewDirectory("mux-config");
                try
                {
                    MuxEndpointListResult? mux = await c.ListMuxEndpointsAsync(muxDir);
                    AssertNotNull(mux, "mux list reply");
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
