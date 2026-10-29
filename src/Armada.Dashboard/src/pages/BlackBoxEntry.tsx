import { Link, useParams } from 'react-router-dom';
import { useLocale } from '../context/LocaleContext';
import PageHeader from '../components/shared/PageHeader';
import BlackBoxPanel from '../components/blackbox/BlackBoxPanel';

/** /blackbox/:id: one black box entry on its own page, with Evidence (range-loaded object viewer) and Notes open. */
export default function BlackBoxEntry() {
  const { t } = useLocale();
  const { id } = useParams<{ id: string }>();
  if (!id) return null;
  return (
    <div>
      <PageHeader
        breadcrumb={<><Link to="/blackbox">{t('Black Box')}</Link> <span className="breadcrumb-sep">&gt;</span> <span className="mono">{id}</span></>}
        title={t('Black box entry')}
        subtitle={t('Where, how and why a mission or voyage failed, with the evidence and the ways to recover.')}
      />
      <BlackBoxPanel key={id} entryId={id} variant="page" />
    </div>
  );
}
