import { lazy, Suspense, useEffect, type ReactNode } from 'react';
import { useSearchParams } from 'react-router-dom';
import Tabs, { type TabDef } from '../components/shared/Tabs';

const History = lazy(() => import('./History'));
const RequestHistory = lazy(() => import('./RequestHistory'));
const Events = lazy(() => import('./Events'));
const Signals = lazy(() => import('./Signals'));
const TokenUsage = lazy(() => import('./TokenUsage'));
const CaptainUsage = lazy(() => import('../components/usage/UsageActivityTabs').then((module) => ({ default: module.CaptainUsageTab })));
const Jobs = lazy(() => import('./Jobs'));

function panel(node: ReactNode): ReactNode {
  return <Suspense fallback={<p className="text-dim" style={{ padding: '1rem' }}>Loading...</p>}>{node}</Suspense>;
}

/**
 * Activity log. Unifies the four "what happened" surfaces behind one source
 * selector so an operator stops guessing which of History, Requests, Events, or
 * Signals to open. "All activity" is the cross-entity History superset; the
 * Requests source keeps its KPIs, activity chart, and request inspector modal;
 * Events and Signals keep their audit/message detail routes. The active source
 * lives in the URL (?source=), so Needs You and Dashboard links land here
 * pre-filtered. Background jobs are the Jobs source (?source=jobs; /jobs
 * redirects there).
 */
export default function Activity() {
  const [params, setParams] = useSearchParams();
  // Keep old account-limit and nested usage bookmarks on the consolidated page.
  const source = params.get('source');
  const legacyTab = params.get('tab');
  useEffect(() => {
    const nested = (!source || source === 'tokens') && (legacyTab === 'overview' || legacyTab === 'limits');
    if (source === 'usage-limits' || nested) {
      const next = new URLSearchParams(params);
      next.set('source', 'usage-overview');
      if (source === 'usage-limits' || legacyTab === 'limits') next.set('section', 'account-limits');
      next.delete('tab');
      setParams(next, { replace: true });
    }
  }, [source, legacyTab, params, setParams]);
  const tabs: TabDef[] = [
    { key: 'history', label: 'All Activity', tooltip: 'One timeline of everything that happened: missions, voyages, merges, deployments, and more.', render: () => panel(<History />) },
    { key: 'requests', label: 'API Requests', tooltip: 'REST API requests the server received, with method, path, status, and timing.', render: () => panel(<RequestHistory />) },
    { key: 'events', label: 'Events', tooltip: 'The server\'s event log: state changes such as missions starting, finishing, or failing.', render: () => panel(<Events />) },
    { key: 'signals', label: 'Signals', tooltip: 'Messages sent between the Admiral and captains.', render: () => panel(<Signals />) },
    { key: 'tokens', label: 'Token Usage', tooltip: 'Model tokens and cost used by captains, by day, captain, vessel, and model.', render: () => panel(<TokenUsage />) },
    { key: 'usage-overview', label: 'Captain Usage', render: () => panel(<CaptainUsage />) },
    { key: 'jobs', label: 'Jobs', tooltip: 'Background jobs such as vessel imports and health checks, with their status, progress, and Cancel.', render: () => panel(<Jobs />) },
  ];

  return <Tabs tabs={tabs} param="source" defaultTabKey="history" ariaLabel="Activity sources" />;
}
