import { lazy, Suspense } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider } from './context/AuthContext';
import { LocaleProvider } from './context/LocaleContext';
import { ThemeProvider } from './context/ThemeContext';
import { WebSocketProvider } from './context/WebSocketContext';
import { NotificationProvider } from './context/NotificationContext';
import ProtectedRoute from './components/ProtectedRoute';
import LoadingIndicator from './components/shared/LoadingIndicator';
import Layout from './components/Layout';
import './App.css';

const Dashboard = lazy(() => import('./pages/Dashboard'));
const Fleets = lazy(() => import('./pages/Fleets'));
const Vessels = lazy(() => import('./pages/Vessels'));
const Captains = lazy(() => import('./pages/Captains'));
const Missions = lazy(() => import('./pages/Missions'));
const Voyages = lazy(() => import('./pages/Voyages'));
const Events = lazy(() => import('./pages/Events'));
const MergeQueue = lazy(() => import('./pages/MergeQueue'));
const Jobs = lazy(() => import('./pages/Jobs'));
const Docks = lazy(() => import('./pages/Docks'));
const Doctor = lazy(() => import('./pages/Doctor'));
const Tenants = lazy(() => import('./pages/admin/Tenants'));
const Users = lazy(() => import('./pages/admin/Users'));
const Credentials = lazy(() => import('./pages/admin/Credentials'));
const Dispatch = lazy(() => import('./pages/Dispatch'));
const Planning = lazy(() => import('./pages/Planning'));
const Objectives = lazy(() => import('./pages/Objectives'));
const ObjectiveDetail = lazy(() => import('./pages/ObjectiveDetail'));
const Signals = lazy(() => import('./pages/Signals'));
const Server = lazy(() => import('./pages/Server'));
const FleetDetail = lazy(() => import('./pages/FleetDetail'));
const VesselDetail = lazy(() => import('./pages/VesselDetail'));
const VesselOnboarding = lazy(() => import('./pages/VesselOnboarding'));
const CaptainDetail = lazy(() => import('./pages/CaptainDetail'));
const MissionDetail = lazy(() => import('./pages/MissionDetail'));
const VoyageDetail = lazy(() => import('./pages/VoyageDetail'));
const VoyageCreate = lazy(() => import('./pages/VoyageCreate'));
const SignalDetail = lazy(() => import('./pages/SignalDetail'));
const EventDetail = lazy(() => import('./pages/EventDetail'));
const DockDetail = lazy(() => import('./pages/DockDetail'));
const MergeQueueDetail = lazy(() => import('./pages/MergeQueueDetail'));
const Personas = lazy(() => import('./pages/Personas'));
const PersonaDetail = lazy(() => import('./pages/PersonaDetail'));
const Pipelines = lazy(() => import('./pages/Pipelines'));
const PipelineDetail = lazy(() => import('./pages/PipelineDetail'));
const PromptTemplates = lazy(() => import('./pages/PromptTemplates'));
const PromptTemplateDetail = lazy(() => import('./pages/PromptTemplateDetail'));
const Playbooks = lazy(() => import('./pages/Playbooks'));
const PlaybookDetail = lazy(() => import('./pages/PlaybookDetail'));
const WorkflowProfiles = lazy(() => import('./pages/WorkflowProfiles'));
const WorkflowProfileDetail = lazy(() => import('./pages/WorkflowProfileDetail'));
const ProjectProfiles = lazy(() => import('./pages/ProjectProfiles'));
const ProjectProfileDetail = lazy(() => import('./pages/ProjectProfileDetail'));
const Skills = lazy(() => import('./pages/Skills'));
const SkillDetail = lazy(() => import('./pages/SkillDetail'));
const AskArmada = lazy(() => import('./pages/AskArmada'));
const Inbox = lazy(() => import('./pages/Inbox'));
const CliPermissions = lazy(() => import('./pages/CliPermissions'));
const CheckRuns = lazy(() => import('./pages/CheckRuns'));
const CheckRunDetail = lazy(() => import('./pages/CheckRunDetail'));
const Environments = lazy(() => import('./pages/Environments'));
const EnvironmentDetail = lazy(() => import('./pages/EnvironmentDetail'));
const Deployments = lazy(() => import('./pages/Deployments'));
const DeploymentDetail = lazy(() => import('./pages/DeploymentDetail'));
const Releases = lazy(() => import('./pages/Releases'));
const ReleaseDetail = lazy(() => import('./pages/ReleaseDetail'));
const Incidents = lazy(() => import('./pages/Incidents'));
const IncidentDetail = lazy(() => import('./pages/IncidentDetail'));
const Runbooks = lazy(() => import('./pages/Runbooks'));
const RunbookDetail = lazy(() => import('./pages/RunbookDetail'));
const Workspace = lazy(() => import('./pages/Workspace'));
const RequestHistory = lazy(() => import('./pages/RequestHistory'));
const ApiExplorer = lazy(() => import('./pages/ApiExplorer'));
const History = lazy(() => import('./pages/History'));
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

function RouteFallback() {
  return <LoadingIndicator fullHeight label="Loading page..." />;
}

export default function App() {
  return (
    <LocaleProvider>
      <ThemeProvider>
        <AuthProvider>
          <WebSocketProvider>
            <NotificationProvider>
              <BrowserRouter basename="/dashboard">
                <Suspense fallback={<RouteFallback />}>
                  <Routes>
                    <Route element={<ProtectedRoute><Layout /></ProtectedRoute>}>
                      <Route index element={<Dashboard />} />
                      <Route path="dashboard" element={<Navigate to="/" replace />} />
                      <Route path="planning" element={<Planning />} />
                      <Route path="planning/:id" element={<Planning />} />
                      <Route path="dispatch" element={<DispatchHub />} />
                      <Route path="backlog" element={<Navigate to="/dispatch?tab=backlog" replace />} />
                      <Route path="backlog/:id" element={<ObjectiveDetail />} />
                      <Route path="objectives" element={<Objectives />} />
                      <Route path="objectives/:id" element={<ObjectiveDetail />} />

                      <Route path="fleets" element={<Navigate to="/vessels?tab=fleets" replace />} />
                      <Route path="fleets/:id" element={<FleetDetail />} />

                      <Route path="vessels" element={<VesselsHub />} />
                      <Route path="vessels/import" element={<VesselsHub />} />
                      <Route path="vessels/health" element={<VesselsHub />} />
                      <Route path="vessels/:id" element={<VesselDetail />} />
                      <Route path="vessels/:id/onboarding" element={<VesselOnboarding />} />
                      <Route path="workspace" element={<Navigate to="/vessels?tab=workspace" replace />} />
                      <Route path="workspace/:vesselId" element={<Workspace />} />
                      <Route path="workspace/:vesselId/:panel" element={<Workspace />} />

                      <Route path="captains" element={<CaptainsHub />} />
                      <Route path="captains/:id" element={<CaptainDetail />} />

                      <Route path="missions" element={<MissionsHub />} />
                      <Route path="missions/:id" element={<MissionDetail />} />

                      <Route path="voyages" element={<Navigate to="/missions?tab=voyages" replace />} />
                      <Route path="voyages/create" element={<VoyageCreate />} />
                      <Route path="voyages/:id" element={<VoyageDetail />} />

                      <Route path="activity" element={<Activity />} />
                      <Route path="signals" element={<Navigate to="/activity?source=signals" replace />} />
                      <Route path="history" element={<Navigate to="/activity?source=history" replace />} />
                      <Route path="signals/:id" element={<SignalDetail />} />

                      <Route path="events" element={<Navigate to="/activity?source=events" replace />} />
                      <Route path="events/:id" element={<EventDetail />} />

                      <Route path="docks" element={<Navigate to="/captains?tab=docks" replace />} />
                      <Route path="docks/:id" element={<DockDetail />} />

                      <Route path="merge-queue" element={<Navigate to="/missions?tab=merge-queue" replace />} />
                      <Route path="merge-queue/:id" element={<MergeQueueDetail />} />

                      <Route path="jobs" element={<Jobs />} />

                      <Route path="configuration" element={<Configuration />} />
                      <Route path="personas" element={<Navigate to="/configuration?tab=personas" replace />} />
                      <Route path="personas/:name" element={<PersonaDetail />} />
                      <Route path="pipelines" element={<Navigate to="/configuration?tab=pipelines" replace />} />
                      <Route path="pipelines/:name" element={<PipelineDetail />} />
                      <Route path="prompt-templates" element={<Navigate to="/configuration?tab=prompts" replace />} />
                      <Route path="prompt-templates/create" element={<PromptTemplateDetail />} />
                      <Route path="prompt-templates/:name" element={<PromptTemplateDetail />} />
                      <Route path="playbooks" element={<Navigate to="/configuration?tab=playbooks" replace />} />
                      <Route path="playbooks/:id" element={<PlaybookDetail />} />
                      <Route path="workflow-profiles" element={<Navigate to="/configuration?tab=workflow-profiles" replace />} />
                      <Route path="workflow-profiles/:id" element={<WorkflowProfileDetail />} />
                      <Route path="project-profiles" element={<Navigate to="/configuration?tab=project-profiles" replace />} />
                      <Route path="project-profiles/:id" element={<ProjectProfileDetail />} />
                      <Route path="skills" element={<Navigate to="/configuration?tab=skills" replace />} />
                      <Route path="skills/:id" element={<SkillDetail />} />
                      {/* One optional-segment route so moving between /ask and /ask/:threadId keeps the page mounted. */}
                      <Route path="ask/:threadId?" element={<AskArmada />} />
                      <Route path="inbox" element={<Inbox />} />
                      <Route path="cli-permissions" element={<CliPermissions />} />
                      <Route path="delivery" element={<DeliveryHub />} />
                      <Route path="fleet-actions" element={<FleetActions />} />
                      <Route path="fleet-actions/runs/:id" element={<FleetActionRunDetail />} />
                      <Route path="checks" element={<Navigate to="/delivery?tab=checks" replace />} />
                      <Route path="checks/:id" element={<CheckRunDetail />} />
                      <Route path="environments" element={<Navigate to="/delivery?tab=environments" replace />} />
                      <Route path="environments/:id" element={<EnvironmentDetail />} />
                      <Route path="deployments" element={<Navigate to="/delivery?tab=deployments" replace />} />
                      <Route path="deployments/:id" element={<DeploymentDetail />} />
                      <Route path="releases" element={<Navigate to="/delivery?tab=releases" replace />} />
                      <Route path="releases/new" element={<ReleaseDetail />} />
                      <Route path="releases/:id" element={<ReleaseDetail />} />
                      <Route path="incidents" element={<Navigate to="/delivery?tab=incidents" replace />} />
                      <Route path="incidents/:id" element={<IncidentDetail />} />
                      <Route path="runbooks" element={<Navigate to="/delivery?tab=runbooks" replace />} />
                      <Route path="runbooks/:id" element={<RunbookDetail />} />
                      <Route path="requests" element={<Navigate to="/activity?source=requests" replace />} />
                      <Route path="requests/:id" element={<RequestHistory />} />
                      <Route path="api-explorer" element={<ApiExplorer />} />
                      <Route path="api-explorer/:operationId" element={<ApiExplorer />} />

                      <Route path="notifications" element={<Navigate to="/inbox" replace />} />

                      <Route path="admin/tenants" element={<Navigate to="/server?tab=tenants" replace />} />
                      <Route path="admin/users" element={<Navigate to="/server?tab=users" replace />} />
                      <Route path="admin/credentials" element={<Navigate to="/server?tab=credentials" replace />} />

                      <Route path="server" element={<ServerHub />} />
                      <Route path="doctor" element={<Navigate to="/server?tab=diagnostics" replace />} />
                      <Route path="settings" element={<Navigate to="/server" replace />} />
                    </Route>
                  </Routes>
                </Suspense>
              </BrowserRouter>
            </NotificationProvider>
          </WebSocketProvider>
        </AuthProvider>
      </ThemeProvider>
    </LocaleProvider>
  );
}
