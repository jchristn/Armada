import { useParams } from 'react-router-dom';
import { useLocale } from '../context/LocaleContext';
import PageHeader from '../components/shared/PageHeader';
import BlackBoxPanel from '../components/blackbox/BlackBoxPanel';
import { BLACKBOX_PATH } from '../lib/navTargets';

/** /blackbox/:id: one black box entry on its own page, with Evidence (range-loaded object viewer) and Notes open. */
export default function BlackBoxEntry() {
  const { t } = useLocale();
  const { id } = useParams<{ id: string }>();
  if (!id) return null;
  return (
    <div>
      <PageHeader
        breadcrumb={[
          { label: t('Black Box'), to: BLACKBOX_PATH },
          { label: id },
        ]}
        title={t('Black box entry')}
        subtitle={t('Where, how and why a mission or voyage failed, with the evidence and the ways to recover.')}
      />
      <BlackBoxPanel key={id} entryId={id} variant="page" />
    </div>
  );
}
