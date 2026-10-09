import type { AskCommandItem } from '../../lib/askCommands';
import { useLocale } from '../../context/LocaleContext';

export const QUICK_MENU_ID = 'ask-quick-actions';

export function quickOptionId(item: AskCommandItem): string {
  return `ask-quick-${item.key.replace(/[^a-zA-Z0-9_-]/g, '-')}`;
}

interface AskQuickActionMenuProps {
  items: AskCommandItem[];
  activeIndex: number;
  onHover: (index: number) => void;
  onChoose: (item: AskCommandItem) => void;
}

/**
 * The `/` menu above the composer: quick actions and the local commands (lib/askCommands). It is a listbox owned by
 * the composer input (combobox pattern): the input keeps focus, Arrow keys move the active option, Enter or Tab
 * runs it, Escape closes.
 */
export default function AskQuickActionMenu({ items, activeIndex, onHover, onChoose }: AskQuickActionMenuProps) {
  const { t } = useLocale();
  return (
    <div className="ask-quick-menu">
      <div className="ask-quick-menu-head text-dim">{t('Commands')}</div>
      <ul id={QUICK_MENU_ID} role="listbox" aria-label={t('Commands')}>
        {items.map((item, index) => (
          <li
            key={item.key}
            id={quickOptionId(item)}
            role="option"
            aria-selected={index === activeIndex}
            className={`ask-quick-option${index === activeIndex ? ' is-active' : ''}`}
            onMouseEnter={() => onHover(index)}
            onMouseDown={(e) => { e.preventDefault(); onChoose(item); }}
          >
            <code className="ask-quick-command">{item.command}{item.usage ? <span className="ask-quick-usage"> {item.usage}</span> : null}</code>
            <span className="ask-quick-title">{t(item.title)}</span>
            {item.description && <span className="ask-quick-desc text-dim">{t(item.description)}</span>}
          </li>
        ))}
      </ul>
    </div>
  );
}
