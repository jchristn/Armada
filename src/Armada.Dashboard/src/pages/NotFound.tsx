import { Link } from 'react-router-dom';
import PageHeader from '../components/shared/PageHeader';
import { useLocale } from '../context/LocaleContext';

/** Unknown or retired deep links always have a visible recovery path inside the dashboard shell. */
export default function NotFound() {
  const { t } = useLocale();
  return (
    <section>
      <PageHeader title={t('Page not found')} subtitle={t('This page does not exist or has moved. Choose a destination from the navigation or return to the dashboard.')} />
      <Link className="btn btn-primary" to="/">{t('Return to Dashboard')}</Link>
    </section>
  );
}
