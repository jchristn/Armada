import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider } from './context/AuthContext';
import { ThemeProvider } from './context/ThemeContext';
import { WebSocketProvider } from './context/WebSocketContext';
import { NotificationProvider } from './context/NotificationContext';
import ProtectedRoute from './components/ProtectedRoute';
import Layout from './components/Layout';

// Lazy-load pages — other agents own these files.
// We import them directly since they already exist as simple components.
import Dashboard from './pages/Dashboard';
import Fleets from './pages/Fleets';
import Vessels from './pages/Vessels';
import Captains from './pages/Captains';
import Missions from './pages/Missions';
import Voyages from './pages/Voyages';
import Events from './pages/Events';
import MergeQueue from './pages/MergeQueue';
import Docks from './pages/Docks';
import Doctor from './pages/Doctor';
import Tenants from './pages/admin/Tenants';
import Users from './pages/admin/Users';
import Credentials from './pages/admin/Credentials';
import './App.css';

import Dispatch from './pages/Dispatch';
import Signals from './pages/Signals';
import Notifications from './pages/Notifications';
import Server from './pages/Server';
import FleetDetail from './pages/FleetDetail';
import VesselDetail from './pages/VesselDetail';
import CaptainDetail from './pages/CaptainDetail';
import MissionDetail from './pages/MissionDetail';
import VoyageDetail from './pages/VoyageDetail';
import VoyageCreate from './pages/VoyageCreate';
import SignalDetail from './pages/SignalDetail';
import EventDetail from './pages/EventDetail';
import DockDetail from './pages/DockDetail';
import MergeQueueDetail from './pages/MergeQueueDetail';
import Personas from './pages/Personas';
import PersonaDetail from './pages/PersonaDetail';
import Pipelines from './pages/Pipelines';
import PipelineDetail from './pages/PipelineDetail';
import PromptTemplates from './pages/PromptTemplates';
import PromptTemplateDetail from './pages/PromptTemplateDetail';

export default function App() {
  return (
    <ThemeProvider>
      <AuthProvider>
        <WebSocketProvider>
          <NotificationProvider>
          <BrowserRouter basename="/dashboard">
            <Routes>
              <Route element={<ProtectedRoute><Layout /></ProtectedRoute>}>
                {/* Default route redirects to dashboard */}
                <Route index element={<Navigate to="/dashboard" replace />} />

                {/* Operations */}
                <Route path="dashboard" element={<Dashboard />} />
                <Route path="dispatch" element={<Dispatch />} />

                {/* Entities - List and Detail */}
                <Route path="fleets" element={<Fleets />} />
                <Route path="fleets/:id" element={<FleetDetail />} />

                <Route path="vessels" element={<Vessels />} />
                <Route path="vessels/:id" element={<VesselDetail />} />

                <Route path="captains" element={<Captains />} />
                <Route path="captains/:id" element={<CaptainDetail />} />

                <Route path="missions" element={<Missions />} />
                <Route path="missions/:id" element={<MissionDetail />} />

                <Route path="voyages" element={<Voyages />} />
                <Route path="voyages/create" element={<VoyageCreate />} />
                <Route path="voyages/:id" element={<VoyageDetail />} />

                {/* System */}
                <Route path="signals" element={<Signals />} />
                <Route path="signals/:id" element={<SignalDetail />} />

                <Route path="events" element={<Events />} />
                <Route path="events/:id" element={<EventDetail />} />

                <Route path="docks" element={<Docks />} />
                <Route path="docks/:id" element={<DockDetail />} />

                <Route path="merge-queue" element={<MergeQueue />} />
                <Route path="merge-queue/:id" element={<MergeQueueDetail />} />

                <Route path="personas" element={<Personas />} />
                <Route path="personas/:name" element={<PersonaDetail />} />
                <Route path="pipelines" element={<Pipelines />} />
                <Route path="pipelines/:name" element={<PipelineDetail />} />
                <Route path="prompt-templates" element={<PromptTemplates />} />
                <Route path="prompt-templates/:name" element={<PromptTemplateDetail />} />

                <Route path="notifications" element={<Notifications />} />

                {/* Administration */}
                <Route path="admin/tenants" element={<ProtectedRoute><Tenants /></ProtectedRoute>} />
                <Route path="admin/users" element={<ProtectedRoute><Users /></ProtectedRoute>} />
                <Route path="admin/credentials" element={<ProtectedRoute><Credentials /></ProtectedRoute>} />

                {/* Tools */}
                <Route path="server" element={<Server />} />
                <Route path="doctor" element={<Doctor />} />
                <Route path="settings" element={<Navigate to="/server" replace />} />
              </Route>
            </Routes>
          </BrowserRouter>
          </NotificationProvider>
        </WebSocketProvider>
      </AuthProvider>
    </ThemeProvider>
  );
}
