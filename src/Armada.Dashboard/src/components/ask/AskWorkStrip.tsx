import { useState } from 'react';
import type { AskTrackedWork } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import StatusBadge from '../shared/StatusBadge';
import { isWorkActive } from '../../lib/askWork';
import { useEntityTypeLabel } from './AskWorkCard';

interface AskWorkStripProps {
  work: AskTrackedWork[];
  onSelect: (work: AskTrackedWork) => void;
}

/** Collapsible "Work in this conversation" strip: every tracked item with a live status chip. */
export default function AskWorkStrip({ work, onSelect }: AskWorkStripProps) {
  const { t } = useLocale();
  const entityLabel = useEntityTypeLabel();
  const [open, setOpen] = useState(true);
  if (work.length === 0) return null;
  const activeCount = work.filter((w) => isWorkActive(w)).length;

  return (
    <section className="ask-work-strip" aria-label={t('Work in this conversation')}>
      <button type="button" className="ask-work-strip-toggle" aria-expanded={open} onClick={() => setOpen((v) => !v)}>
        <span className={`ask-chevron${open ? ' is-open' : ''}`} aria-hidden="true">&#9656;</span>
        <span>{t('Work in this conversation')}</span>
        <span className="text-dim">
          {activeCount > 0
            ? t('{{active}} active of {{total}}', { active: activeCount, total: work.length })
            : t('{{total}} finished', { total: work.length })}
        </span>
      </button>
      {open && (
        <ul className="ask-work-strip-items">
          {work.map((item) => (
            <li key={item.id}>
              <button
                type="button"
                className="ask-work-chip"
                onClick={() => onSelect(item)}
                title={t('Show the live card for {{title}}', { title: item.title || item.entityId })}
              >
                {isWorkActive(item) && <span className="ask-live-dot" aria-hidden="true" />}
                <span className="ask-work-chip-type text-dim">{entityLabel(item.entityType)}</span>
                <span className="ask-work-chip-title">{item.title || item.entityId}</span>
                {item.status && <StatusBadge status={item.status} />}
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
