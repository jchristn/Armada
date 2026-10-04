import { useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import Tabs, { type TabDef } from '../components/shared/Tabs';
import PageHeader from '../components/shared/PageHeader';
import FleetActionsTable from '../components/fleetActions/FleetActionsTable';
import FleetActionRunsTable from '../components/fleetActions/FleetActionRunsTable';
import { useLocale } from '../context/LocaleContext';

/**
 * Fleet Actions: define reusable commands and mission prompts (Actions tab) and watch their runs across many
 * vessels (Runs tab). `?run=new` opens the run flow (pick vessels, then the run modal), used by the Home CTA.
 */
export default function FleetActions() {
  const { t } = useLocale();
  const [searchParams, setSearchParams] = useSearchParams();
  const startRun = searchParams.get('run') === 'new';

  const clearRunParam = useCallback(() => {
    const next = new URLSearchParams(searchParams);
    next.delete('run');
    setSearchParams(next, { replace: true });
  }, [searchParams, setSearchParams]);

  const tabs: TabDef[] = [
    { key: 'actions', label: 'Actions', render: () => <FleetActionsTable startRunOnMount={startRun} onRunFlowStarted={clearRunParam} /> },
    { key: 'runs', label: 'Runs', render: () => <FleetActionRunsTable /> },
  ];

  return (
    <div className="fleet-actions-page">
      <PageHeader
        title={t('Fleet Actions')}
        subtitle={t('Run a command or a captain mission across many vessels and track the result for each one.')}
      />
      <Tabs tabs={tabs} defaultTabKey="actions" ariaLabel="Fleet action sections" />
    </div>
  );
}
