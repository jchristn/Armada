import type { AskQuickAction } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';

export const QUICK_MENU_ID = 'ask-quick-actions';

export function quickOptionId(action: AskQuickAction): string {
  return `ask-quick-${action.name.replace(/[^a-zA-Z0-9_-]/g, '')}`;
}

interface AskQuickActionMenuProps {
  actions: AskQuickAction[];
  activeIndex: number;
  onHover: (index: number) => void;
  onChoose: (action: AskQuickAction) => void;
}

/**
 * The `/` menu above the composer. It is a listbox owned by the composer input (combobox pattern): the input
 * keeps focus, Arrow keys move the active option, Enter or Tab chooses, Escape closes.
 */
export default function AskQuickActionMenu({ actions, activeIndex, onHover, onChoose }: AskQuickActionMenuProps) {
  const { t } = useLocale();
  return (
    <div className="ask-quick-menu">
      <div className="ask-quick-menu-head text-dim">{t('Quick actions')}</div>
      <ul id={QUICK_MENU_ID} role="listbox" aria-label={t('Quick actions')}>
        {actions.map((action, index) => (
          <li
            key={action.name}
            id={quickOptionId(action)}
            role="option"
            aria-selected={index === activeIndex}
            className={`ask-quick-option${index === activeIndex ? ' is-active' : ''}`}
            onMouseEnter={() => onHover(index)}
            onMouseDown={(e) => { e.preventDefault(); onChoose(action); }}
          >
            <code className="ask-quick-command">{action.command || `/${action.name}`}</code>
            <span className="ask-quick-title">{action.title ? t(action.title) : action.name}</span>
            {action.description && <span className="ask-quick-desc text-dim">{t(action.description)}</span>}
          </li>
        ))}
      </ul>
    </div>
  );
}
