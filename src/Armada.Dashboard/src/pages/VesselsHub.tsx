import { lazy, Suspense, useEffect, type ReactNode } from 'react';
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import Tabs, { type TabDef } from '../components/shared/Tabs';

const Vessels = lazy(() => import('./Vessels'));
const Fleets = lazy(() => import('./Fleets'));
const Workspace = lazy(() => import('./Workspace'));
const VesselHealth = lazy(() => import('./VesselHealth'));

/** Path of the Health tab; it is a real route (not `?tab=`) so its own filter query string stays clean. */
export const VESSEL_HEALTH_PATH = '/vessels/health';

function panel(node: ReactNode): ReactNode {
  return <Suspense fallback={<p className="text-dim" style={{ padding: '1rem' }}>Loading...</p>}>{node}</Suspense>;
}

/**
 * Vessels hub. A fleet is a folder of vessels, so managing the container and its
 * contents belongs on one surface. Vessels is the primary view; Fleets folds in
 * as a tab (create/rename/delete), Health grades every vessel at `/vessels/health`,
 * and Workspace is the per-vessel drill-in.
 * Keeps Armada's nautical vocabulary: the nav item and route stay `/vessels`.
 */
export default function VesselsHub() {
  const location = useLocation();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const onHealthPath = location.pathname.replace(/\/+$/, '') === VESSEL_HEALTH_PATH;
  const requestedTab = searchParams.get('tab');

  // Keep the Health tab on its own route: `/vessels?tab=health` becomes `/vessels/health`, and choosing
  // another tab while on `/vessels/health` returns to `/vessels?tab=<key>` (dropping health filters).
  useEffect(() => {
    if (!onHealthPath && requestedTab === 'health') {
      const next = new URLSearchParams(searchParams);
      next.delete('tab');
      const query = next.toString();
      navigate(`${VESSEL_HEALTH_PATH}${query ? `?${query}` : ''}`, { replace: true });
    } else if (onHealthPath && requestedTab && requestedTab !== 'health') {
      navigate(`/vessels?tab=${encodeURIComponent(requestedTab)}`);
    }
  }, [navigate, onHealthPath, requestedTab, searchParams]);

  const tabs: TabDef[] = [
    { key: 'vessels', label: 'Vessels', render: () => panel(<Vessels />) },
    { key: 'health', label: 'Health', render: () => panel(<VesselHealth />) },
    { key: 'fleets', label: 'Fleets', render: () => panel(<Fleets />) },
    { key: 'workspace', label: 'Workspace', render: () => panel(<Workspace />) },
  ];

  return <Tabs tabs={tabs} defaultTabKey={onHealthPath ? 'health' : 'vessels'} ariaLabel="Vessel sections" />;
}
