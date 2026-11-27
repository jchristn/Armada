import { lazy, Suspense, type ReactNode } from 'react';
import Tabs, { type TabDef } from '../components/shared/Tabs';

const Captains = lazy(() => import('./Captains'));
const Docks = lazy(() => import('./Docks'));
const BatteryPacks = lazy(() => import('./BatteryPacks'));
const ModelProfiles = lazy(() => import('./ModelProfiles'));
const Reliability = lazy(() => import('./Reliability'));
const BatteryRuns = lazy(() => import('./BatteryRuns'));

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
 * and the server enforces AdminOnly on those routes. Model profiles (the models as served) are a tab too; a row
 * opens /model-profiles/:id. Reliability (completion, failure kinds, repairs and escalations per model and persona, and
 * the latest gate report) and Battery Runs (with cancel) are tabs as well.
 */
export default function CaptainsHub() {
  const tabs: TabDef[] = [
    { key: 'captains', label: 'Captains', tooltip: 'The agents (Claude Code, Codex, Gemini, mux, and others) that do mission work, and whether each is idle or working.', render: () => panel(<Captains />) },
    { key: 'profiles', label: 'Model Profiles', tooltip: 'The models as served (runtime, provider, model, context), shared by every captain that runs them: tier, recovery policy, timeouts and capabilities.', render: () => panel(<ModelProfiles />) },
    { key: 'reliability', label: 'Reliability', tooltip: 'How each model does per persona: completion, failure kinds, repairs, escalations and the Unclassified rate, plus the latest acceptance gate report.', render: () => panel(<Reliability />) },
    { key: 'docks', label: 'Docks', tooltip: 'Git worktrees Armada created for captains to work in, one per mission, and their state.', render: () => panel(<Docks />) },
    { key: 'battery', label: 'Battery Packs', tooltip: 'Fixture battery packs a Full captain evaluation runs: validity, language, tasks, and (for admins) manifest and file editing.', render: () => panel(<BatteryPacks />) },
    { key: 'battery-runs', label: 'Battery Runs', tooltip: 'Full captain evaluations and acceptance gate runs, newest first, with cancel for a queued or running run.', render: () => panel(<BatteryRuns />) },
  ];

  return <Tabs tabs={tabs} defaultTabKey="captains" ariaLabel="Captain sections" />;
}
