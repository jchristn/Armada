import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { useLocale } from '../../context/LocaleContext';
import { flattenNavCommands } from '../navConfig';

/**
 * Cmd-K / Ctrl-K launcher. An accelerator, not a replacement for the sidebar:
 * it jumps to any nav destination and gives Ask Armada a fast entry point.
 * Ask Armada keeps its permanent standalone nav slot regardless.
 */
export default function CommandPalette() {
  const navigate = useNavigate();
  const { t } = useLocale();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [selectedIndex, setSelectedIndex] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);

  const commands = useMemo(() => flattenNavCommands(), []);

  const results = useMemo(() => {
    const term = query.trim().toLowerCase();
    if (!term) return commands;
    return commands.filter((cmd) => {
      const label = t(cmd.label).toLowerCase();
      const section = cmd.section ? t(cmd.section).toLowerCase() : '';
      return label.includes(term) || section.includes(term) || cmd.to.toLowerCase().includes(term);
    });
  }, [commands, query, t]);

  const close = useCallback(() => {
    setOpen(false);
    setQuery('');
    setSelectedIndex(0);
  }, []);

  // Global Cmd/Ctrl+K toggles the palette.
  useEffect(() => {
    function handleKeyDown(event: globalThis.KeyboardEvent) {
      if ((event.metaKey || event.ctrlKey) && (event.key === 'k' || event.key === 'K')) {
        event.preventDefault();
        setOpen((prev) => !prev);
      }
    }
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, []);

  useEffect(() => {
    if (open) {
      setSelectedIndex(0);
      // Focus the input after the overlay renders.
      const id = window.setTimeout(() => inputRef.current?.focus(), 0);
      return () => window.clearTimeout(id);
    }
    return undefined;
  }, [open]);

  useEffect(() => {
    setSelectedIndex(0);
  }, [query]);

  const runCommand = useCallback((to: string) => {
    close();
    navigate(to);
  }, [close, navigate]);

  const onKeyDown = useCallback((event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      close();
    } else if (event.key === 'ArrowDown') {
      event.preventDefault();
      setSelectedIndex((prev) => (results.length === 0 ? 0 : (prev + 1) % results.length));
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      setSelectedIndex((prev) => (results.length === 0 ? 0 : (prev - 1 + results.length) % results.length));
    } else if (event.key === 'Enter') {
      event.preventDefault();
      const target = results[selectedIndex];
      if (target) runCommand(target.to);
    }
  }, [close, results, runCommand, selectedIndex]);

  if (!open) return null;

  return (
    <div className="command-palette-overlay" onMouseDown={close}>
      <div
        className="command-palette"
        role="dialog"
        aria-modal="true"
        aria-label={t('Command palette')}
        onMouseDown={(event) => event.stopPropagation()}
        onKeyDown={onKeyDown}
      >
        <input
          ref={inputRef}
          type="text"
          className="command-palette-input"
          placeholder={t('Jump to a page or ask Armada...')}
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          aria-label={t('Search destinations')}
        />
        {results.length > 0 ? (
          <ul className="command-palette-list" aria-label={t('Search destinations')}>
            {results.map((cmd, index) => (
              <li key={cmd.to}>
                <button
                  type="button"
                  className={`command-palette-item${index === selectedIndex ? ' active' : ''}`}
                  aria-current={index === selectedIndex ? 'true' : undefined}
                  onMouseEnter={() => setSelectedIndex(index)}
                  onClick={() => runCommand(cmd.to)}
                >
                  <span className="command-palette-item-label">{t(cmd.label)}</span>
                  {cmd.section && <span className="command-palette-item-section">{t(cmd.section)}</span>}
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <p className="command-palette-empty">{t('No matching destinations.')}</p>
        )}
      </div>
    </div>
  );
}
