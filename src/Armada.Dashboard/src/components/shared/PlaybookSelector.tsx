import { type ChangeEvent, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { listPlaybooks } from '../../api/client';
import type { Playbook, PlaybookDeliveryMode, SelectedPlaybook } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';

const DELIVERY_MODE_COPY: Record<PlaybookDeliveryMode, { label: string; shortLabel: string; description: string }> = {
  InlineFullContent: {
    label: 'Inline Full Content',
    shortLabel: 'Inline',
    description: 'Inject the complete markdown into the mission instructions.',
  },
  InstructionWithReference: {
    label: 'Instruction With Reference',
    shortLabel: 'Reference',
    description: 'Tell the model to read the materialized playbook path outside the worktree.',
  },
  AttachIntoWorktree: {
    label: 'Attach Into Worktree',
    shortLabel: 'Worktree',
    description: 'Materialize the playbook in `.armada/playbooks/` and instruct the model to read it there.',
  },
};

interface PlaybookSelectorProps {
  value: SelectedPlaybook[];
  onChange: (next: SelectedPlaybook[]) => void;
  disabled?: boolean;
}

export default function PlaybookSelector({ value, onChange, disabled = false }: PlaybookSelectorProps) {
  const { t, formatRelativeTime } = useLocale();
  const [playbooks, setPlaybooks] = useState<Playbook[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [availableSelection, setAvailableSelection] = useState<string[]>([]);
  const [selectedSelection, setSelectedSelection] = useState<string[]>([]);

  useEffect(() => {
    let mounted = true;

    async function load() {
      try {
        setLoading(true);
        const result = await listPlaybooks({ pageSize: 9999 });
        if (!mounted) return;
        setPlaybooks(result.objects || []);
        setError('');
      } catch (err: unknown) {
        if (!mounted) return;
        setError(err instanceof Error ? err.message : t('Failed to load playbooks.'));
      } finally {
        if (mounted) setLoading(false);
      }
    }

    load();
    return () => { mounted = false; };
  }, [t]);

  const activePlaybooks = playbooks.filter((playbook) => playbook.active !== false);
  const selectedIds = new Set(value.map((item) => item.playbookId));
  const filteredAvailable = activePlaybooks.filter((playbook) => {
    if (selectedIds.has(playbook.id)) return false;
    const query = search.trim().toLowerCase();
    if (!query) return true;
    return playbook.fileName.toLowerCase().includes(query)
      || (playbook.description || '').toLowerCase().includes(query)
      || playbook.id.toLowerCase().includes(query);
  });

  useEffect(() => {
    const activeIds = new Set(activePlaybooks.map((playbook) => playbook.id));
    setAvailableSelection((current) => current.filter((id) => activeIds.has(id) && !selectedIds.has(id)));
    setSelectedSelection((current) => current.filter((id) => selectedIds.has(id)));
  }, [playbooks, value]);

  function handleListSelection(event: ChangeEvent<HTMLSelectElement>, setter: (ids: string[]) => void) {
    setter(Array.from(event.target.selectedOptions).map((option) => option.value));
  }

  function addSelectedPlaybooks() {
    if (disabled || availableSelection.length === 0) return;
    const selectedSet = new Set(availableSelection);
    const additions = activePlaybooks
      .filter((playbook) => selectedSet.has(playbook.id) && !selectedIds.has(playbook.id))
      .map((playbook) => ({
        playbookId: playbook.id,
        deliveryMode: 'InlineFullContent' as PlaybookDeliveryMode,
      }));

    if (additions.length === 0) return;
    onChange([...value, ...additions]);
    setSelectedSelection(additions.map((item) => item.playbookId));
    setAvailableSelection([]);
  }

  function removeSelectedPlaybooks() {
    if (disabled || selectedSelection.length === 0) return;
    const removalSet = new Set(selectedSelection);
    onChange(value.filter((item) => !removalSet.has(item.playbookId)));
    setSelectedSelection([]);
  }

  function updateModeForSelection(deliveryMode: PlaybookDeliveryMode) {
    if (disabled || selectedSelection.length === 0) return;
    const updateSet = new Set(selectedSelection);
    onChange(value.map((item) => updateSet.has(item.playbookId) ? { ...item, deliveryMode } : item));
  }

  function moveSelected(direction: -1 | 1) {
    if (disabled || selectedSelection.length !== 1) return;
    const playbookId = selectedSelection[0];
    const index = value.findIndex((item) => item.playbookId === playbookId);
    const nextIndex = index + direction;
    if (index < 0 || nextIndex < 0 || nextIndex >= value.length) return;

    const next = [...value];
    const current = next[index];
    next[index] = next[nextIndex];
    next[nextIndex] = current;
    onChange(next);
  }

  function resolvePlaybook(playbookId: string): Playbook | undefined {
    return playbooks.find((playbook) => playbook.id === playbookId);
  }

  const focusedAvailable = filteredAvailable.find((playbook) => playbook.id === availableSelection[0]) || null;
  const focusedSelectedItems = value.filter((item) => selectedSelection.includes(item.playbookId));
  const focusedSelectedPlaybook = selectedSelection.length === 1 ? resolvePlaybook(selectedSelection[0]) || null : null;
  const selectedModes = Array.from(new Set(focusedSelectedItems.map((item) => item.deliveryMode)));
  const selectedModeValue = focusedSelectedItems.length === 0
    ? 'InlineFullContent'
    : selectedModes.length === 1
      ? selectedModes[0]
      : '';
  const selectedIndex = selectedSelection.length === 1
    ? value.findIndex((item) => item.playbookId === selectedSelection[0])
    : -1;

  return (
    <section className="playbook-selector-shell">
      <div className="playbook-selector-head">
        <div>
          <h3>{t('Playbooks')}</h3>
          <p className="text-dim">
            {t('Attach reusable markdown guidance to every mission in this dispatch. Order matters, and each selection can use its own delivery mode.')}
          </p>
        </div>
        <div className="playbook-selector-meta">
          <span>{t('{{count}} available', { count: activePlaybooks.length })}</span>
          <span>{t('{{count}} selected', { count: value.length })}</span>
          <Link to="/playbooks" className="playbook-selector-link">{t('Manage playbooks')}</Link>
        </div>
      </div>

      {error && <div className="alert alert-error">{error}</div>}

      <div className="playbook-selector-layout">
        <div className="playbook-selector-pane">
          <div className="playbook-selector-pane-head">
            <div>
              <h4>{t('Available')}</h4>
              <p className="text-dim">{t('Filter and multi-select active playbooks to attach to this dispatch.')}</p>
            </div>
          </div>

          <div className="playbook-selector-pane-body">
            <input
              type="text"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              className="playbook-selector-search"
              placeholder={t('Search playbooks...')}
              disabled={disabled || loading}
            />

          {loading ? (
            <div className="playbook-selector-empty">{t('Loading playbooks...')}</div>
          ) : activePlaybooks.length === 0 ? (
            <div className="playbook-selector-empty">
              <strong>{t('No active playbooks found.')}</strong>
              <span>{t('Create one from the Playbooks page, then return here to attach it.')}</span>
            </div>
          ) : filteredAvailable.length === 0 ? (
            <div className="playbook-selector-empty">
              <strong>{t('No playbooks match the current filter.')}</strong>
              <span>{t('Try a different search or clear the filter.')}</span>
            </div>
          ) : (
            <>
              <select
                multiple
                size={12}
                className="playbook-selector-listbox"
                value={availableSelection}
                disabled={disabled}
                onChange={(event) => handleListSelection(event, setAvailableSelection)}
              >
                {filteredAvailable.map((playbook) => (
                  <option key={playbook.id} value={playbook.id}>
                    {playbook.fileName}
                  </option>
                ))}
              </select>

              <div className="playbook-selector-details">
                {focusedAvailable ? (
                  <>
                    <strong>{focusedAvailable.fileName}</strong>
                    <span className="text-dim">{focusedAvailable.description || t('No description')}</span>
                    <div className="playbook-selector-detail-meta">
                      <span>{t('{{count}} chars', { count: focusedAvailable.content.length.toLocaleString() })}</span>
                      <span>{t('Updated {{time}}', { time: formatRelativeTime(focusedAvailable.lastUpdateUtc) })}</span>
                    </div>
                  </>
                ) : (
                  <span className="text-dim">{t('Select one or more playbooks to review them before adding.')}</span>
                )}
              </div>
            </>
          )}
          </div>
        </div>

        <div className="playbook-selector-toolbar">
          <button type="button" className="btn" disabled={disabled || availableSelection.length === 0} onClick={addSelectedPlaybooks}>
            {t('Add')} &gt;
          </button>
          <button type="button" className="btn" disabled={disabled || selectedSelection.length === 0} onClick={removeSelectedPlaybooks}>
            &lt; {t('Remove')}
          </button>
          <button type="button" className="btn" disabled={disabled || selectedSelection.length !== 1 || selectedIndex <= 0} onClick={() => moveSelected(-1)}>
            {t('Up')}
          </button>
          <button
            type="button"
            className="btn"
            disabled={disabled || selectedSelection.length !== 1 || selectedIndex < 0 || selectedIndex >= value.length - 1}
            onClick={() => moveSelected(1)}
          >
            {t('Down')}
          </button>
        </div>

        <div className="playbook-selector-pane">
          <div className="playbook-selector-pane-head">
            <div>
              <h4>{t('Selected')}</h4>
              <p className="text-dim">{t('These playbooks will be applied in the listed order when the work is dispatched.')}</p>
            </div>
          </div>

          <div className="playbook-selector-pane-body">
          {value.length === 0 ? (
            <div className="playbook-selector-empty emphasized">
              <strong>{t('No playbooks selected yet.')}</strong>
              <span>{t('Use this when you want repeatable engineering rules, architecture constraints, or review standards to be applied automatically.')}</span>
            </div>
          ) : (
            <>
              <select
                multiple
                size={12}
                className="playbook-selector-listbox"
                value={selectedSelection}
                disabled={disabled}
                onChange={(event) => handleListSelection(event, setSelectedSelection)}
              >
                {value.map((item, index) => {
                  const playbook = resolvePlaybook(item.playbookId);
                  return (
                    <option key={`${item.playbookId}-${index}`} value={item.playbookId}>
                      {`${index + 1}. ${playbook?.fileName || item.playbookId} [${t(DELIVERY_MODE_COPY[item.deliveryMode].shortLabel)}]`}
                    </option>
                  );
                })}
              </select>

              <div className="playbook-selector-details">
                {focusedSelectedItems.length === 0 ? (
                  <span className="text-dim">{t('Select one or more attached playbooks to change delivery mode or review details.')}</span>
                ) : (
                  <>
                    <strong>
                      {focusedSelectedItems.length === 1
                        ? (focusedSelectedPlaybook?.fileName || selectedSelection[0])
                        : t('{{count}} playbooks selected', { count: focusedSelectedItems.length })}
                    </strong>
                    {focusedSelectedItems.length === 1 && (
                      <span className="text-dim">{focusedSelectedPlaybook?.description || t('No description')}</span>
                    )}
                    <div className="playbook-selector-detail-meta">
                      {focusedSelectedItems.length === 1 && focusedSelectedPlaybook && (
                        <span>{t('{{count}} chars', { count: focusedSelectedPlaybook.content.length.toLocaleString() })}</span>
                      )}
                      <span>{t('Order is preserved in the final instruction bundle.')}</span>
                    </div>
                    <label className="playbook-mode-field">
                      <span>{t('Delivery Mode')}</span>
                      <select
                        value={selectedModeValue}
                        disabled={disabled || focusedSelectedItems.length === 0}
                        onChange={(event) => updateModeForSelection(event.target.value as PlaybookDeliveryMode)}
                      >
                        {selectedModes.length > 1 && <option value="" disabled>{t('Multiple values')}</option>}
                        {Object.entries(DELIVERY_MODE_COPY).map(([mode, copy]) => (
                          <option key={mode} value={mode}>
                            {t(copy.label)}
                          </option>
                        ))}
                      </select>
                    </label>
                    <span className="text-dim">
                      {selectedModes.length > 1
                        ? t('Choosing a mode here applies it to every highlighted playbook.')
                        : t(DELIVERY_MODE_COPY[(selectedModes[0] || 'InlineFullContent') as PlaybookDeliveryMode].description)}
                    </span>
                  </>
                )}
              </div>
            </>
          )}
          </div>
        </div>
      </div>
    </section>
  );
}
