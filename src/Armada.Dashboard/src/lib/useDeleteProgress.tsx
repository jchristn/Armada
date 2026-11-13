import { useCallback, useMemo, useRef, useState, type ReactElement } from 'react';
import { useLocale } from '../context/LocaleContext';
import DeleteProgressDialog from '../components/shared/DeleteProgressDialog';
import { deleteProgressMessage, deleteProgressTitle, type DeleteProgressKind } from './deleteProgress';

/** One delete (or bulk delete, or purge) to run behind the progress modal. */
export interface DeleteProgressOptions {
  /** What is being deleted; picks the title ("Deleting vessel", "Deleting 12 missions"). */
  kind: DeleteProgressKind;
  /** Number of records for a bulk delete; omit for a single record. */
  count?: number;
  /** Already-localized title that replaces the one picked from kind and count. */
  title?: string;
  /** Already-localized progress message that replaces the default "This may take some time..." text. */
  message?: string;
  /** The delete itself. A rejection turns the modal into an error with Dismiss and Retry; Retry calls it again. */
  action: () => Promise<unknown>;
  /** Localized failure text for an error; defaults to the server's message, else "Delete failed." */
  errorMessage?: (err: unknown) => string;
}

/** What {@link useDeleteProgress} returns. */
export interface DeleteProgress {
  /**
   * Open the progress modal and run the delete. Resolves true once it succeeds (after any retries) and the modal
   * has closed, or false when the user dismissed the failure. Resolves false at once, without calling the action,
   * when another delete from this hook is still pending or showing its failure (double clicks, a second row).
   */
  run: (options: DeleteProgressOptions) => Promise<boolean>;
  /** True while a delete is pending or its failure is still shown. Read synchronously, before React re-renders. */
  isBusy: () => boolean;
  /** The modal; render it once in the page. */
  dialog: ReactElement;
}

interface ActiveDelete {
  title: string;
  message: string;
  error: string;
}

/**
 * The server's message for an ApiError (the same rule as apiErrorMessage in api/client), read by name so pages whose
 * tests mock the API client still get the fallback text.
 */
function serverMessage(err: unknown): string {
  return err instanceof Error && err.name === 'ApiError' && err.message ? err.message : '';
}

/**
 * Run deletes behind {@link DeleteProgressDialog}: the modal opens as soon as the delete starts, cannot be
 * dismissed while it is pending, closes on success, and becomes an error with Dismiss and Retry on failure. Keep the
 * page's confirmation dialog as it is and call {@link DeleteProgress.run} from its confirm handler.
 */
export function useDeleteProgress(): DeleteProgress {
  const { t } = useLocale();
  const [active, setActive] = useState<ActiveDelete | null>(null);
  const optionsRef = useRef<DeleteProgressOptions | null>(null);
  const resolveRef = useRef<((completed: boolean) => void) | null>(null);
  const activeRef = useRef(false);
  const runningRef = useRef(false);
  const tRef = useRef(t);
  tRef.current = t;

  const attempt = useCallback(async () => {
    const options = optionsRef.current;
    if (!options || runningRef.current) return;
    runningRef.current = true;
    const translate = tRef.current;
    const title = options.title ?? deleteProgressTitle(translate, options.kind, options.count);
    const message = options.message ?? deleteProgressMessage(translate);
    setActive({ title, message, error: '' });
    try {
      await options.action();
      runningRef.current = false;
      activeRef.current = false;
      optionsRef.current = null;
      setActive(null);
      const resolve = resolveRef.current;
      resolveRef.current = null;
      resolve?.(true);
    } catch (err: unknown) {
      runningRef.current = false;
      const error = options.errorMessage ? options.errorMessage(err) : serverMessage(err) || translate('Delete failed.');
      setActive({ title, message, error: error || translate('Delete failed.') });
    }
  }, []);

  const run = useCallback((options: DeleteProgressOptions): Promise<boolean> => {
    if (activeRef.current) return Promise.resolve(false);
    activeRef.current = true;
    optionsRef.current = options;
    return new Promise<boolean>((resolve) => {
      resolveRef.current = resolve;
      void attempt();
    });
  }, [attempt]);

  const dismiss = useCallback(() => {
    if (runningRef.current || !activeRef.current) return;
    activeRef.current = false;
    optionsRef.current = null;
    setActive(null);
    const resolve = resolveRef.current;
    resolveRef.current = null;
    resolve?.(false);
  }, []);

  const retry = useCallback(() => { void attempt(); }, [attempt]);
  const isBusy = useCallback(() => activeRef.current, []);

  const dialog = useMemo(() => (
    <DeleteProgressDialog
      open={!!active}
      title={active?.title ?? ''}
      message={active?.message ?? ''}
      error={active?.error ?? ''}
      onRetry={retry}
      onDismiss={dismiss}
    />
  ), [active, retry, dismiss]);

  return { run, isBusy, dialog };
}
