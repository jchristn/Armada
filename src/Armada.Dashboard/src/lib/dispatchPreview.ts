import { useEffect, useRef, useState } from 'react';
import { lintMissionBrief, previewMissionOverlap } from '../api/client';
import type { BriefLintFinding, BriefLintResult, MissionOverlapPreview, MissionOverlapWarning } from '../types/blackbox';

/** Debounce before a typed brief is previewed, so a keystroke does not cost a request. */
export const DISPATCH_PREVIEW_DELAY_MS = 600;

/** Warnings dispatch returned on a created mission or voyage (both lists are transient and only on the response). */
export interface DispatchWarnings {
  overlapWarnings: MissionOverlapWarning[];
  briefLintWarnings: BriefLintFinding[];
}

/** The warnings on a create response, or null when it has none. */
export function dispatchWarningsFrom(result: { overlapWarnings?: MissionOverlapWarning[] | null; briefLintWarnings?: BriefLintFinding[] | null } | null | undefined): DispatchWarnings | null {
  const overlapWarnings = result?.overlapWarnings ?? [];
  const briefLintWarnings = result?.briefLintWarnings ?? [];
  if (overlapWarnings.length === 0 && briefLintWarnings.length === 0) return null;
  return { overlapWarnings, briefLintWarnings };
}

/** Navigation state that carries a create response's warnings to the voyage or mission page. */
export interface DispatchWarningsState { dispatchWarnings?: DispatchWarnings | null }

export function dispatchWarningsFromState(state: unknown): DispatchWarnings | null {
  if (!state || typeof state !== 'object') return null;
  const value = (state as DispatchWarningsState).dispatchWarnings;
  return value ? dispatchWarningsFrom(value) : null;
}

export interface DispatchPreviewInput {
  vesselId?: string | null;
  title?: string | null;
  description?: string | null;
  dependsOnMissionId?: string | null;
  excludeMissionId?: string | null;
  /** Off skips every request (for example while the form is submitting). */
  enabled?: boolean;
  delayMs?: number;
}

export type DispatchPreviewStatus = 'idle' | 'loading' | 'ready' | 'error';

export interface DispatchPreviewState {
  status: DispatchPreviewStatus;
  /** The overlap preview (when a vessel is chosen); it carries the brief lint result too. */
  preview: MissionOverlapPreview | null;
  /** The lint result (from the preview, or from lint-brief when no vessel is chosen). */
  lint: BriefLintResult | null;
  error: string;
}

const IDLE: DispatchPreviewState = { status: 'idle', preview: null, lint: null, error: '' };

/**
 * Live, debounced preview of what dispatch would warn about: POST /api/v1/vessels/{id}/overlap-preview when a vessel
 * is chosen (overlaps plus brief lint), otherwise POST /api/v1/missions/lint-brief. Nothing is dispatched. A response
 * that arrives after newer input is dropped.
 */
export function useDispatchPreview(input: DispatchPreviewInput): DispatchPreviewState {
  const { vesselId, title, description, dependsOnMissionId, excludeMissionId, enabled = true, delayMs = DISPATCH_PREVIEW_DELAY_MS } = input;
  const [state, setState] = useState<DispatchPreviewState>(IDLE);
  const sequence = useRef(0);

  useEffect(() => {
    const text = `${title ?? ''}${description ?? ''}`.trim();
    const current = ++sequence.current;
    if (!enabled || !text) {
      setState(IDLE);
      return;
    }
    setState((s) => ({ ...s, status: 'loading', error: '' }));
    const timer = setTimeout(() => {
      const request: Promise<{ preview: MissionOverlapPreview | null; lint: BriefLintResult | null }> = Promise.resolve().then(() => vesselId
        ? previewMissionOverlap(vesselId, { title, description, dependsOnMissionId, excludeMissionId }).then((preview) => ({ preview, lint: preview?.briefLint ?? null }))
        : lintMissionBrief({ title, description, dependsOnMissionId }).then((lint) => ({ preview: null, lint })));
      request
        .then((result) => { if (current === sequence.current) setState({ status: 'ready', preview: result.preview, lint: result.lint, error: '' }); })
        .catch((err: unknown) => { if (current === sequence.current) setState({ status: 'error', preview: null, lint: null, error: err instanceof Error ? err.message : String(err) }); });
    }, delayMs);
    return () => clearTimeout(timer);
  }, [vesselId, title, description, dependsOnMissionId, excludeMissionId, enabled, delayMs]);

  return state;
}
