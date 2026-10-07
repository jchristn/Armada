import { useLocale } from '../../context/LocaleContext';
import { statusDescription } from '../../lib/statusDescriptions';

interface StatusBadgeProps {
  status: string;
  className?: string;
}

export default function StatusBadge({ status, className = '' }: StatusBadgeProps) {
  const { t } = useLocale();
  const normalized = (status || '').toLowerCase();
  const description = statusDescription(normalized);
  const tooltip = description ? t(description) : '';

  return (
    <span
      className={`tag ${normalized} ${className}`.trim()}
      title={tooltip}
    >
      {t(status)}
    </span>
  );
}
