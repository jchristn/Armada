import { useState } from 'react';
import { useLocale } from '../../context/LocaleContext';
import { TEMPLATE_VARIABLES } from '../../lib/fleetActionLabels';
import DataTable from '../shared/DataTable';

interface TemplateVariableHelpProps {
  /** Start expanded. */
  defaultOpen?: boolean;
  /** When set, clicking a variable inserts it (e.g. at the end of the body field). */
  onInsert?: (token: string) => void;
}

/** Collapsible list of the template variables the server renders, with optional click-to-insert. */
export default function TemplateVariableHelp({ defaultOpen = false, onInsert }: TemplateVariableHelpProps) {
  const { t } = useLocale();
  const [open, setOpen] = useState(defaultOpen);

  return (
    <div className="template-help">
      <button type="button" className="collapsible-header template-help-toggle" aria-expanded={open} onClick={() => setOpen(!open)}>
        <span className="collapsible-caret" aria-hidden="true">{open ? '-' : '+'}</span>
        <span>{t('Template variables')}</span>
      </button>
      {open && (
        <>
          <p className="text-dim form-help">
            {t('Variables are substituted per vessel in a single pass, without shell escaping. Names are case-insensitive. Any other {{name}} is rejected.')}
          </p>
          <DataTable
            tableKey="template-variables"
            className="template-help-table"
            recordCount={null}
            rows={TEMPLATE_VARIABLES}
            rowKey={(v) => v.name}
            columns={[
              {
                key: 'variable', label: t('Variable'), required: true,
                render: (v) => {
                  const token = `{{${v.name}}}`;
                  return onInsert ? (
                    <button type="button" className="btn btn-sm mono template-help-token" onClick={() => onInsert(token)} title={t('Insert {{token}}', { token })} aria-label={t('Insert {{token}}', { token })} data-i18n-skip="true">
                      {token}
                    </button>
                  ) : (
                    <code>{token}</code>
                  );
                },
              },
              { key: 'description', label: t('Description'), required: true, cellClassName: 'text-dim', render: (v) => t(v.description) },
            ]}
          />
        </>
      )}
    </div>
  );
}
