import { lazy, Suspense, type ReactNode } from 'react';
import Tabs, { type TabDef } from '../components/shared/Tabs';

const Captains = lazy(() => import('./Captains'));
const Docks = lazy(() => import('./Docks'));
const BatteryPacks = lazy(() => import('./BatteryPacks'));

function panel(node: ReactNode): ReactNode {
  return <Suspense fallback={<p className="text-dim" style={{ padding: '1rem' }}>Loading...</p>}>{node}</Suspense>;
}

/**
 * Captains hub. Docks are git worktrees, an implementation detail of how a
 * captain runs a mission, so they move off the top level into a tab here for the
 * rare cleanup case. Keeps the nautical vocabulary: the nav item and route stay
 * `/captains`. Battery packs (the fixture packs a Full captain evaluation runs) are a
 * third tab; the old `/battery` route redirects to `/captains?tab=battery`. Every
 * signed-in user can view packs; BatteryPacks gates create/edit/delete on isAdmin
 * and the server enforces AdminOnly on those routes.
 */
export default function CaptainsHub() {
  const tabs: TabDef[] = [
    { key: 'captains', label: 'Captains', tooltip: 'The agents (Claude Code, Codex, Gemini, mux, and others) that do mission work, and whether each is idle or working.', render: () => panel(<Captains />) },
    { key: 'docks', label: 'Docks', tooltip: 'Git worktrees Armada created for captains to work in, one per mission, and their state.', render: () => panel(<Docks />) },
    { key: 'battery', label: 'Battery Packs', tooltip: 'Fixture battery packs a Full captain evaluation runs: validity, language, tasks, and (for admins) manifest and file editing.', render: () => panel(<BatteryPacks />) },
  ];

  return <Tabs tabs={tabs} defaultTabKey="captains" ariaLabel="Captain sections" />;
}
