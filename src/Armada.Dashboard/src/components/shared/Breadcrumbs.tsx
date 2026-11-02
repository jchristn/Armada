import { Link } from 'react-router-dom';
import { useLocale } from '../../context/LocaleContext';

export interface BreadcrumbItem {
  label: string;
  to?: string;
  title?: string;
}

/** One dashboard breadcrumb structure: linked ancestors, a > separator, and a plain current page. */
export default function Breadcrumbs({ items }: { items: readonly BreadcrumbItem[] }) {
  const { t } = useLocale();
  if (!items.length) return null;
  return (
    <nav className="breadcrumb" aria-label={t('Breadcrumbs')}>
      <ol>
        {items.map((item, index) => (
          <li key={index}>
            {index > 0 && <span className="breadcrumb-sep" aria-hidden="true">&gt;</span>}
            {index < items.length - 1 && item.to
              ? <Link to={item.to} title={item.title}>{item.label}</Link>
              : <span aria-current={index === items.length - 1 ? 'page' : undefined}>{item.label}</span>}
          </li>
        ))}
      </ol>
    </nav>
  );
}
