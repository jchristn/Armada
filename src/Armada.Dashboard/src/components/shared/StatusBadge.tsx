import { useLocale } from '../../context/LocaleContext';
import { statusDescription } from '../../lib/statusDescriptions';

interface StatusBadgeProps {
  status: string;
  className?: string;
  /** Tooltip that replaces the status description, for example the reasons behind an Invalid status. */
  title?: string;
}

export default function StatusBadge({ status, className = '', title }: StatusBadgeProps) {
  const { t } = useLocale();
  const normalized = (status || '').toLowerCase();
  const description = statusDescription(normalized);
  const tooltip = description ? t(description) : '';

  return (
    <span
      className={`tag ${normalized} ${className}`.trim()}
      title={title || tooltip}
    >
      {t(status)}
    </span>
  );
}
