import { lazy } from 'react';
import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { AuthProvider } from './context/AuthContext';
import { LocaleProvider } from './context/LocaleContext';
import { ThemeProvider } from './context/ThemeContext';
import { WebSocketProvider } from './context/WebSocketContext';
import { NotificationProvider } from './context/NotificationContext';
import ProtectedRoute from './components/ProtectedRoute';
import Layout from './components/Layout';
import { BlackBoxRedirect, CliPermissionsRedirect, PlanningRedirect, LegacyPageRedirect } from './components/LegacyNavRedirects';
import './App.css';

const NotFound = lazy(() => import('./pages/NotFound'));
const Dashboard = lazy(() => import('./pages/Dashboard'));
const ObjectiveDetail = lazy(() => import('./pages/ObjectiveDetail'));
const FleetDetail = lazy(() => import('./pages/FleetDetail'));
const VesselDetail = lazy(() => import('./pages/VesselDetail'));
const VesselOnboarding = lazy(() => import('./pages/VesselOnboarding'));
const VesselHistory = lazy(() => import('./pages/VesselHistory'));
const CaptainDetail = lazy(() => import('./pages/CaptainDetail'));
const MissionDetail = lazy(() => import('./pages/MissionDetail'));
const VoyageDetail = lazy(() => import('./pages/VoyageDetail'));
const VoyageCreate = lazy(() => import('./pages/VoyageCreate'));
const SignalDetail = lazy(() => import('./pages/SignalDetail'));
const EventDetail = lazy(() => import('./pages/EventDetail'));
const DockDetail = lazy(() => import('./pages/DockDetail'));
const MergeQueueDetail = lazy(() => import('./pages/MergeQueueDetail'));
const PersonaDetail = lazy(() => import('./pages/PersonaDetail'));
const PipelineDetail = lazy(() => import('./pages/PipelineDetail'));
const PromptTemplateDetail = lazy(() => import('./pages/PromptTemplateDetail'));
const PlaybookDetail = lazy(() => import('./pages/PlaybookDetail'));
const WorkflowProfileDetail = lazy(() => import('./pages/WorkflowProfileDetail'));
const ProjectProfileDetail = lazy(() => import('./pages/ProjectProfileDetail'));
const SkillDetail = lazy(() => import('./pages/SkillDetail'));
const AskArmada = lazy(() => import('./pages/AskArmada'));
const Inbox = lazy(() => import('./pages/Inbox'));
const CheckRunDetail = lazy(() => import('./pages/CheckRunDetail'));
const EnvironmentDetail = lazy(() => import('./pages/EnvironmentDetail'));
const DeploymentDetail = lazy(() => import('./pages/DeploymentDetail'));
const ReleaseDetail = lazy(() => import('./pages/ReleaseDetail'));
const IncidentDetail = lazy(() => import('./pages/IncidentDetail'));
const RunbookDetail = lazy(() => import('./pages/RunbookDetail'));
const Workspace = lazy(() => import('./pages/Workspace'));
const RequestHistory = lazy(() => import('./pages/RequestHistory'));
const ApiExplorer = lazy(() => import('./pages/ApiExplorer'));
const Configuration = lazy(() => import('./pages/Configuration'));
const Activity = lazy(() => import('./pages/Activity'));
const ServerHub = lazy(() => import('./pages/ServerHub'));
const VesselsHub = lazy(() => import('./pages/VesselsHub'));
const CaptainsHub = lazy(() => import('./pages/CaptainsHub'));
const MissionsHub = lazy(() => import('./pages/MissionsHub'));
const DispatchHub = lazy(() => import('./pages/DispatchHub'));
const DeliveryHub = lazy(() => import('./pages/DeliveryHub'));
const FleetActions = lazy(() => import('./pages/FleetActions'));
const FleetActionRunDetail = lazy(() => import('./pages/FleetActionRunDetail'));
const BlackBoxEntry = lazy(() => import('./pages/BlackBoxEntry'));
const Evaluations = lazy(() => import('./pages/Evaluations'));
const EvaluationDetail = lazy(() => import('./pages/EvaluationDetail'));

export default function App() {
  return (
    <LocaleProvider>
      <ThemeProvider>
        <AuthProvider>
          <WebSocketProvider>
            <NotificationProvider>
              <BrowserRouter basename="/dashboard">
                <Routes>
                  <Route element={<ProtectedRoute><Layout /></ProtectedRoute>}>
                    <Route index element={<Dashboard />} />
                    <Route path="dashboard" element={<LegacyPageRedirect to="/" />} />
                    <Route path="planning" element={<PlanningRedirect />} />
                    <Route path="planning/:id" element={<PlanningRedirect />} />
                    <Route path="dispatch" element={<DispatchHub />} />
                    <Route path="backlog" element={<LegacyPageRedirect to="/dispatch?tab=backlog" />} />
                    <Route path="backlog/:id" element={<ObjectiveDetail />} />
                    <Route path="objectives" element={<LegacyPageRedirect to="/dispatch?tab=backlog" />} />
                    <Route path="objectives/:id" element={<ObjectiveDetail />} />

                    <Route path="fleets" element={<LegacyPageRedirect to="/vessels?tab=fleets" />} />
                    <Route path="fleets/:id" element={<FleetDetail />} />

                    <Route path="vessels" element={<VesselsHub />} />
                    <Route path="vessels/import" element={<VesselsHub />} />
                    <Route path="vessels/health" element={<VesselsHub />} />
                    <Route path="vessels/:id" element={<VesselDetail />} />
                    <Route path="vessels/:id/onboarding" element={<VesselOnboarding />} />
                    <Route path="vessels/:id/history" element={<VesselHistory />} />
                    <Route path="workspace" element={<LegacyPageRedirect to="/vessels?tab=workspace" />} />
                    <Route path="workspace/:vesselId" element={<Workspace />} />
                    <Route path="workspace/:vesselId/:panel" element={<Workspace />} />

                    <Route path="battery" element={<LegacyPageRedirect to="/captains?tab=battery" />} />
                    <Route path="captains" element={<CaptainsHub />} />
                    <Route path="captains/:id" element={<CaptainDetail />} />
                    <Route path="evaluations" element={<Evaluations />} />
                    <Route path="evaluations/:id" element={<EvaluationDetail />} />

                    <Route path="missions" element={<MissionsHub />} />
                    <Route path="missions/:id" element={<MissionDetail />} />

                    <Route path="voyages" element={<LegacyPageRedirect to="/missions?tab=voyages" />} />
                    <Route path="voyages/create" element={<VoyageCreate />} />
                    <Route path="voyages/:id" element={<VoyageDetail />} />

                    <Route path="activity" element={<Activity />} />
                    <Route path="signals" element={<LegacyPageRedirect to="/activity?source=signals" />} />
                    <Route path="history" element={<LegacyPageRedirect to="/activity?source=history" />} />
                    <Route path="signals/:id" element={<SignalDetail />} />

                    <Route path="events" element={<LegacyPageRedirect to="/activity?source=events" />} />
                    <Route path="events/:id" element={<EventDetail />} />

                    <Route path="docks" element={<LegacyPageRedirect to="/captains?tab=docks" />} />
                    <Route path="docks/:id" element={<DockDetail />} />

                    <Route path="merge-queue" element={<LegacyPageRedirect to="/missions?tab=merge-queue" />} />
                    <Route path="merge-queue/:id" element={<MergeQueueDetail />} />

                    <Route path="jobs" element={<LegacyPageRedirect to="/activity?source=events" />} />

                    <Route path="configuration" element={<Configuration />} />
                    <Route path="personas" element={<LegacyPageRedirect to="/configuration?tab=personas" />} />
                    <Route path="personas/:name" element={<PersonaDetail />} />
                    <Route path="pipelines" element={<LegacyPageRedirect to="/configuration?tab=pipelines" />} />
                    <Route path="pipelines/:name" element={<PipelineDetail />} />
                    <Route path="prompt-templates" element={<LegacyPageRedirect to="/configuration?tab=prompts" />} />
                    <Route path="prompt-templates/create" element={<PromptTemplateDetail />} />
                    <Route path="prompt-templates/:name" element={<PromptTemplateDetail />} />
                    <Route path="playbooks" element={<LegacyPageRedirect to="/configuration?tab=playbooks" />} />
                    <Route path="playbooks/:id" element={<PlaybookDetail />} />
                    <Route path="workflow-profiles" element={<LegacyPageRedirect to="/configuration?tab=workflow-profiles" />} />
                    <Route path="workflow-profiles/:id" element={<WorkflowProfileDetail />} />
                    <Route path="project-profiles" element={<LegacyPageRedirect to="/configuration?tab=project-profiles" />} />
                    <Route path="project-profiles/:id" element={<ProjectProfileDetail />} />
                    <Route path="skills" element={<LegacyPageRedirect to="/configuration?tab=skills" />} />
                    <Route path="skills/:id" element={<SkillDetail />} />
                    {/* One optional-segment route so moving between /ask and /ask/:threadId keeps the page mounted. */}
                    <Route path="ask/:threadId?" element={<AskArmada />} />
                    <Route path="inbox" element={<Inbox />} />
                    <Route path="blackbox" element={<BlackBoxRedirect />} />
                    <Route path="blackbox/:id" element={<BlackBoxEntry />} />
                    <Route path="cli-permissions" element={<CliPermissionsRedirect />} />
                    <Route path="delivery" element={<DeliveryHub />} />
                    <Route path="fleet-actions" element={<FleetActions />} />
                    <Route path="fleet-actions/runs/:id" element={<FleetActionRunDetail />} />
                    <Route path="checks" element={<LegacyPageRedirect to="/delivery?tab=checks" />} />
                    <Route path="checks/:id" element={<CheckRunDetail />} />
                    <Route path="environments" element={<LegacyPageRedirect to="/delivery?tab=environments" />} />
                    <Route path="environments/:id" element={<EnvironmentDetail />} />
                    <Route path="deployments" element={<LegacyPageRedirect to="/delivery?tab=deployments" />} />
                    <Route path="deployments/:id" element={<DeploymentDetail />} />
                    <Route path="releases" element={<LegacyPageRedirect to="/delivery?tab=releases" />} />
                    <Route path="releases/new" element={<ReleaseDetail />} />
                    <Route path="releases/:id" element={<ReleaseDetail />} />
                    <Route path="incidents" element={<LegacyPageRedirect to="/delivery?tab=incidents" />} />
                    <Route path="incidents/:id" element={<IncidentDetail />} />
                    <Route path="runbooks" element={<LegacyPageRedirect to="/delivery?tab=runbooks" />} />
                    <Route path="runbooks/:id" element={<RunbookDetail />} />
                    <Route path="requests" element={<LegacyPageRedirect to="/activity?source=requests" />} />
                    <Route path="requests/:id" element={<RequestHistory />} />
                    <Route path="api-explorer" element={<ApiExplorer />} />
                    <Route path="api-explorer/:operationId" element={<ApiExplorer />} />

                    <Route path="notifications" element={<LegacyPageRedirect to="/inbox" />} />

                    <Route path="admin/tenants" element={<LegacyPageRedirect to="/server?tab=tenants" />} />
                    <Route path="admin/users" element={<LegacyPageRedirect to="/server?tab=users" />} />
                    <Route path="admin/credentials" element={<LegacyPageRedirect to="/server?tab=credentials" />} />

                    <Route path="storage" element={<LegacyPageRedirect to="/server?tab=storage" />} />
                    <Route path="server" element={<ServerHub />} />
                    <Route path="doctor" element={<LegacyPageRedirect to="/server?tab=diagnostics" />} />
                    <Route path="settings" element={<LegacyPageRedirect to="/server" />} />
                    <Route path="*" element={<NotFound />} />
                  </Route>
                </Routes>
              </BrowserRouter>
            </NotificationProvider>
          </WebSocketProvider>
        </AuthProvider>
      </ThemeProvider>
    </LocaleProvider>
  );
}
