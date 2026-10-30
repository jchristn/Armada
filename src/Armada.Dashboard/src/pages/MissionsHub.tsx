import { lazy, Suspense, type ReactNode } from 'react';
import Tabs, { type TabDef } from '../components/shared/Tabs';

const Missions = lazy(() => import('./Missions'));
const Voyages = lazy(() => import('./Voyages'));
const MergeQueue = lazy(() => import('./MergeQueue'));
const BlackBox = lazy(() => import('./BlackBox'));

function panel(node: ReactNode): ReactNode {
  return <Suspense fallback={<p className="text-dim" style={{ padding: '1rem' }}>Loading...</p>}>{node}</Suspense>;
}

/**
 * Missions surface. Missions, Voyages, and the merge queue are the same
 * operational flow at three grains, so they share one surface: Missions is the
 * default view, with Voyages, the full Merge Queue, and the Black Box (failure
 * records, whose own sections use `?bbtab=`) as tabs. Attention-worthy merge
 * entries also surface in Needs You (backend inbox).
 */
export default function MissionsHub() {
  const tabs: TabDef[] = [
    { key: 'missions', label: 'Missions', tooltip: 'Individual units of work assigned to captains, with their status, branch, and output.', render: () => panel(<Missions />) },
    { key: 'voyages', label: 'Voyages', tooltip: 'Batches of related missions dispatched together, with their combined progress.', render: () => panel(<Voyages />) },
    { key: 'merge-queue', label: 'Merge Queue', tooltip: 'Finished mission branches waiting to be tested and merged into their vessel\'s default branch.', render: () => panel(<MergeQueue />) },
    { key: 'blackbox', label: 'Black Box', tooltip: 'Failure records for missions and voyages: where, how and why each failed, and how to recover', render: () => panel(<BlackBox />) },
  ];

  return <Tabs tabs={tabs} defaultTabKey="missions" ariaLabel="Mission sections" />;
}
