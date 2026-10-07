import { useCallback, useState, type ReactElement } from 'react';
import { useLocale } from '../../i18n/LocaleContext';
import { ConfirmDialog } from '../ui';

export interface ConfirmRequest {
  title: string;
  message: string;
  confirmLabel?: string;
  danger?: boolean;
  /** The dashboard's typed confirmation (for example 'delete'). */
  typedConfirmation?: string;
  onConfirm: () => void | Promise<void>;
}

/**
 * A confirmation dialog driven by a call (`ask({...})`), like the dashboard pages' confirm state. Render the
 * returned element once in the screen.
 */
export function useConfirm(testID = 'confirm'): [ReactElement, (request: ConfirmRequest) => void] {
  const { t } = useLocale();
  const [request, setRequest] = useState<ConfirmRequest | null>(null);
  const ask = useCallback((next: ConfirmRequest) => setRequest(next), []);
  const element = (
    <ConfirmDialog
      open={request !== null}
      title={request?.title ?? ''}
      message={request?.message ?? ''}
      confirmLabel={request?.confirmLabel ?? t('Confirm')}
      cancelLabel={t('Cancel')}
      danger={request?.danger}
      typedConfirmation={request?.typedConfirmation}
      typedLabel={request?.typedConfirmation ? t('Type "{{word}}" to confirm', { word: request.typedConfirmation }) : undefined}
      onCancel={() => setRequest(null)}
      onConfirm={() => {
        const current = request;
        setRequest(null);
        if (current) void current.onConfirm();
      }}
      testID={testID}
    />
  );
  return [element, ask];
}
