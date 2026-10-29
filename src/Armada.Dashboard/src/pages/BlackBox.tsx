import { useEffect, useState } from 'react';
import { listVessels } from '../api/client';
import { listAll } from '../api/paging';
import type { Vessel } from '../types/models';
import { useLocale } from '../context/LocaleContext';
import PageHeader from '../components/shared/PageHeader';
import Tabs from '../components/shared/Tabs';
import BlackBoxEntriesTab from '../components/blackbox/BlackBoxEntriesTab';
import BlackBoxFingerprintsTab from '../components/blackbox/BlackBoxFingerprintsTab';
import BlackBoxHotspotsTab from '../components/blackbox/BlackBoxHotspotsTab';
import BlackBoxRetentionTab from '../components/blackbox/BlackBoxRetentionTab';
import BlackBoxRecoveryStatsTab from '../components/blackbox/BlackBoxRecoveryStatsTab';
import '../components/blackbox/BlackBox.css';

/**
 * The Black Box page (MISSION_BLACKBOX_PLAN.md 11.1): every mission and voyage failure record, with tabs Entries
 * (filters, search, saved filters), Fingerprints (failure modes and their trend by Armada version), Hotspots
 * (directories failures keep touching), Recovery stats (success rates of recovery paths) and Retention (policy, usage, garbage collection and purge).
 */
export default function BlackBox() {
  const { t } = useLocale();
  const [vessels, setVessels] = useState<Vessel[]>([]);

  useEffect(() => {
    listAll(listVessels).then((r) => setVessels(r.objects ?? [])).catch(() => setVessels([]));
  }, []);

  return (
    <div>
      <PageHeader
        title={t('Black Box')}
        subtitle={t('Every mission and voyage failure: where, how and why it failed, what would have prevented it, and how to recover.')}
      />
      <Tabs
        ariaLabel="Black box sections"
        tabs={[
          { key: 'entries', label: 'Entries', tooltip: 'Failure records, newest first, with filters and saved filters.', render: () => <BlackBoxEntriesTab vessels={vessels} /> },
          { key: 'fingerprints', label: 'Fingerprints', tooltip: 'Failure modes: entries that failed the same way, with their trend by Armada version.', render: () => <BlackBoxFingerprintsTab vessels={vessels} /> },
          { key: 'hotspots', label: 'Hotspots', tooltip: 'Directories that keep appearing in failures, per vessel.', render: () => <BlackBoxHotspotsTab vessels={vessels} /> },
          { key: 'recovery', label: 'Recovery stats', tooltip: 'How often each recovery path recovered the mission, by rule and path.', render: () => <BlackBoxRecoveryStatsTab vessels={vessels} /> },
          { key: 'retention', label: 'Retention', tooltip: 'Retention policy, storage usage, garbage collection and purge.', render: () => <BlackBoxRetentionTab vessels={vessels} /> },
        ]}
      />
    </div>
  );
}
