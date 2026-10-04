import { useState } from 'react';
import { useLocale } from '../../context/LocaleContext';

interface ListEditorProps {
  id: string;
  /** Already-localized label. */
  label: string;
  /** Already-localized help text. */
  help?: string;
  placeholder?: string;
  values: string[];
  onChange: (values: string[]) => void;
  mono?: boolean;
}

/** Editable list of strings: add with Enter or the Add button, remove per item. Duplicates are ignored. */
export default function ListEditor({ id, label, help, placeholder, values, onChange, mono = false }: ListEditorProps) {
  const { t } = useLocale();
  const [draft, setDraft] = useState('');

  function add() {
    const value = draft.trim();
    if (!value) return;
    if (!values.includes(value)) onChange([...values, value]);
    setDraft('');
  }

  return (
    <div className="form-group list-editor">
      <label htmlFor={`${id}-input`}>{label}</label>
      <div className="list-editor-add">
        <input
          id={`${id}-input`}
          className={mono ? 'mono' : undefined}
          value={draft}
          placeholder={placeholder}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); add(); } }}
          aria-describedby={help ? `${id}-help` : undefined}
        />
        <button type="button" className="btn btn-sm" onClick={add} disabled={!draft.trim()}>{t('Add')}</button>
      </div>
      {help && <span id={`${id}-help`} className="text-dim form-help">{help}</span>}
      {values.length === 0 ? (
        <p className="text-dim list-editor-empty">{t('None')}</p>
      ) : (
        <ul className="list-editor-items" aria-label={label}>
          {values.map((v) => (
            <li key={v} className="list-editor-item">
              <span className={mono ? 'mono' : undefined} data-i18n-skip="true">{v}</span>
              <button type="button" className="btn btn-sm list-editor-remove" onClick={() => onChange(values.filter((x) => x !== v))} aria-label={t('Remove {{value}}', { value: v })} title={t('Remove {{value}}', { value: v })}>
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" aria-hidden="true"><path d="M18 6 6 18" /><path d="m6 6 12 12" /></svg>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
