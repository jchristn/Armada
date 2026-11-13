import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, authorizeCaptainSelectionRecovery, getCaptainSelection, getCaptainSelectionHistory, getCaptainSelectionDecision, previewCaptainSelection } from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';
import type { CaptainSelectionDecisionDetail, CaptainSelectionDecisionView, CaptainSelectionSummary } from '../../types/models';
import CaptainSelectionCard from './CaptainSelectionCard';

vi.mock('../../api/client', () => ({
  isApiStatus: (error: { status?: number }, status: number) => error.status === status, apiErrorMessage: (error: Error) => error.message, authorizeCaptainSelectionRecovery: vi.fn(), getCaptainSelection: vi.fn(), getCaptainSelectionHistory: vi.fn(), getCaptainSelectionDecision: vi.fn(), previewCaptainSelection: vi.fn(),
  ApiError: class ApiError extends Error { constructor(message: string, public status: number, public data: unknown) { super(message); } },
}));
const locale = {
  locale: 'en', t: (key: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', key, null, params),
  formatDateTime: (value: string | null | undefined) => value ?? '',
};
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => locale }));
const decision: CaptainSelectionDecisionView = { id: 'csd_current', tenantId: 'tenant', missionId: 'msn_one', trigger: 'Dispatch', outcome: 'Admitted',
  mode: 'Observe', strategy: 'MostRemainingAllowance', selectedCaptainId: 'cpt_one', selectedCaptainName: 'Stored captain', selectedCaptainAvailable: true,
  intendedModel: 'configured-model', reportedModel: 'runtime-model', hypotheticalCaptainId: 'cpt_two', reasonCode: 'SelectedForHeadroom',
  algorithmVersion: 'usage-bands-v1', policyRevisionId: 'policy', evaluatedUtc: '2026-11-03T12:00:00Z' };
const summary: CaptainSelectionSummary = { missionId: 'msn_one', currentMode: 'Observe', currentStrategy: 'MostRemainingAllowance', historyAvailable: true, currentAttempt: decision, latestDecision: decision };
const details: CaptainSelectionDecisionDetail = { decision,
  policy: { mode: 'Observe', strategy: 'MostRemainingAllowance', reservePercent: 10, perAttemptReservationPercent: 2 } as CaptainSelectionDecisionDetail['policy'],
  candidates: { success: true, pageNumber: 1, pageSize: 10, totalPages: 2, totalRecords: 11, totalMs: 1, objects: [
    { captainId: 'cpt_one', captainName: 'Stored captain', captainAvailable: true, model: 'configured-model', rank: 2, band: 'Constrained', reasonCode: 'SufficientHeadroom', availablePercent: 20, canSelect: true, actualSelection: true, hypotheticalSelection: false },
    { captainId: 'cpt_two', captainName: 'Deleted captain', captainAvailable: false, model: 'another-model', rank: 1, band: 'Comfortable', reasonCode: 'SufficientHeadroom', availablePercent: 80, canSelect: true, actualSelection: false, hypotheticalSelection: true },
  ] }, executionEvents: { success: true, pageNumber: 1, pageSize: 25, totalPages: 0, totalRecords: 0, totalMs: 0, objects: [] } };
function mount(status = 'InProgress') { return render(<MemoryRouter><CaptainSelectionCard missionId="msn_one" status={status} /></MemoryRouter>); }

describe('mission captain selection', () => {
  beforeEach(() => {
    vi.mocked(getCaptainSelection).mockReset().mockResolvedValue(structuredClone(summary));
    vi.mocked(getCaptainSelectionDecision).mockReset().mockResolvedValue(structuredClone(details));
    vi.mocked(getCaptainSelectionHistory).mockReset().mockResolvedValue({ success: true, objects: [{ ...decision, id: 'csd_old', selectedCaptainName: 'Historical captain' }], pageNumber: 1, pageSize: 10, totalPages: 2, totalRecords: 11, totalMs: 1 });
    vi.mocked(previewCaptainSelection).mockReset().mockResolvedValue({ missionId: 'msn_one', mode: 'Observe', strategy: 'MostRemainingAllowance', reasonCode: 'SelectedForHeadroom', evaluatedUtc: '2026-11-03T13:00:00Z', candidates: structuredClone(details.candidates) });
  });
  it('keeps long recovery explanations collapsed and interpolates the audit revision', async () => {
    vi.mocked(getCaptainSelection).mockResolvedValue({ ...summary, recovery: { id: 'ura_retained', missionId: 'msn_one', actorUserId: 'admin', reason: 'Detailed operator rationale', revision: 7, allowFallback: true, reserveException: false, createdUtc: decision.evaluatedUtc } });
    const { container } = mount('Pending');
    expect(await screen.findByText('Recovery revision 7')).toBeInTheDocument();
    const audit = container.querySelector('details.selection-recovery-record');
    expect(audit).not.toHaveAttribute('open');
    expect(screen.getByText('Detailed operator rationale', { exact: false })).not.toBeVisible();
    fireEvent.click(within(audit as HTMLElement).getByText('Recovery revision 7'));
    // jsdom does not implement details activation; the native summary semantics own keyboard expansion in the browser review.
    expect(audit?.querySelector('summary')).toBeInTheDocument();
  });
  it('requires an audit reason and sends only explicit administrator recovery permissions', async () => {
    vi.mocked(getCaptainSelection).mockResolvedValue({ ...summary, canManageRecovery: true, recovery: null });
    vi.mocked(authorizeCaptainSelectionRecovery).mockResolvedValue({ id: 'ura_one', missionId: 'msn_one', actorUserId: 'admin', reason: 'Controlled recovery', revision: 1, allowFallback: true, reserveException: false, createdUtc: '2026-11-03T12:00:00Z' });
    mount('Pending');
    fireEvent.click(await screen.findByRole('button', { name: 'Recovery permissions' }));
    const dialog = screen.getByRole('dialog', { name: 'Recovery permissions' });
    expect(within(dialog).getByRole('button', { name: 'Save permissions' })).toBeDisabled();
    expect(within(dialog).getByRole('checkbox', { name: /Allow soft-reserve exception/ })).not.toBeChecked();
    fireEvent.click(within(dialog).getByRole('checkbox', { name: /Allow eligible fallback/ }));
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Reason' }), { target: { value: 'Controlled recovery' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save permissions' }));
    await waitFor(() => expect(authorizeCaptainSelectionRecovery).toHaveBeenCalledWith('msn_one', { expectedRevision: 0, allowFallback: true, reserveException: false, reason: 'Controlled recovery' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });
  it('reloads a conflicting recovery revision while keeping the operator reason', async () => {
    vi.mocked(getCaptainSelection).mockResolvedValue({ ...summary, canManageRecovery: true });
    vi.mocked(authorizeCaptainSelectionRecovery).mockRejectedValue(new ApiError('Refresh before retrying.', 409, null));
    mount('Pending'); fireEvent.click(await screen.findByRole('button', { name: 'Recovery permissions' }));
    const dialog = screen.getByRole('dialog');
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Reason' }), { target: { value: 'Reason to keep' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save permissions' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Refresh before retrying.');
    expect(within(dialog).getByRole('textbox')).toHaveValue('Reason to keep');
    vi.mocked(getCaptainSelection).mockResolvedValue({ ...summary, canManageRecovery: true, recovery: { id: 'ura_peer', missionId: 'msn_one', actorUserId: 'peer', reason: 'Peer updated permissions', revision: 2, allowFallback: false, reserveException: false, createdUtc: decision.evaluatedUtc } });
    vi.mocked(authorizeCaptainSelectionRecovery).mockResolvedValue({ id: 'ura_updated', missionId: 'msn_one', actorUserId: 'admin', reason: 'Reason to keep', revision: 3, allowFallback: false, reserveException: false, createdUtc: decision.evaluatedUtc });
    expect(within(dialog).getByRole('button', { name: 'Save permissions' })).toBeDisabled();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Refresh captain selection' }));
    await waitFor(() => expect(within(dialog).queryByRole('alert')).toBeNull());
    expect(within(dialog).getByRole('textbox')).toHaveValue('Reason to keep');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save permissions' }));
    await waitFor(() => expect(authorizeCaptainSelectionRecovery).toHaveBeenLastCalledWith('msn_one', { expectedRevision: 2, allowFallback: false, reserveException: false, reason: 'Reason to keep' }));

  });
  it('clears cached evidence when recovery permission and mission access are revoked', async () => {
    vi.mocked(getCaptainSelection).mockResolvedValue({ ...summary, canManageRecovery: true });
    vi.mocked(authorizeCaptainSelectionRecovery).mockRejectedValue(new ApiError('Denied', 403, null));
    mount('Pending'); await screen.findByText('runtime-model');
    fireEvent.click(screen.getByRole('button', { name: 'Recovery permissions' }));
    const dialog = screen.getByRole('dialog');
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Reason' }), { target: { value: 'Permission revoked while editing' } });
    vi.mocked(getCaptainSelection).mockRejectedValueOnce(new ApiError('Denied', 403, null));
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save permissions' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(await screen.findByRole('alert')).toHaveTextContent('Selection details could not be refreshed.');
    expect(screen.queryByText('runtime-model')).toBeNull();
  });
  it('does not present recovery mutations to viewers or running work', async () => {
    mount('Pending'); await screen.findByText('runtime-model');
    expect(screen.queryByRole('button', { name: 'Recovery permissions' })).toBeNull();
  });
  it('labels preview choices as proposed rather than admitted selections', async () => {
    mount('Pending');
    await screen.findByText('runtime-model');
    fireEvent.click(screen.getByRole('button', { name: 'Preview current selection' }));
    const table = await screen.findByRole('table', { name: 'Comparison' });
    await waitFor(() => expect(within(table).getAllByText('Proposed')).toHaveLength(2));
    expect(within(table).queryByText('Selected')).not.toBeInTheDocument();
  });
  it('separates intended and reported models and only links currently visible captains', async () => {
    mount();
    await screen.findByText('runtime-model');
    expect(screen.getByText('configured-model')).toBeInTheDocument();
    expect(screen.getByText(/The current routing chose this captain/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Compare captains' }));
    const table = await screen.findByRole('table', { name: 'Comparison' });
    await within(table).findByText('Deleted captain');
    expect(within(table).getAllByRole('columnheader')).toHaveLength(3);
    expect(within(table).getByRole('link', { name: 'Stored captain' })).toHaveAttribute('href', '/captains/cpt_one');
    expect(within(table).queryByRole('link', { name: 'Deleted captain' })).not.toBeInTheDocument();
    expect(within(table).getByText('Effective headroom: 80%')).toBeInTheDocument();
    expect(screen.getByText('Protected reserve 10% · Estimate per attempt 2%')).toBeInTheDocument();
  });
  it('counts unlisted candidates by reason only when the decision omitted some', async () => {
    vi.mocked(getCaptainSelectionDecision).mockResolvedValue({ ...structuredClone(details),
      omittedCandidates: [{ reasonCode: 'BelowReserve', count: 812 }, { reasonCode: 'UsageStale', count: 187 }] });
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Compare captains' }));
    const summaryLine = await screen.findByText('Other captains considered but not listed: 999');
    const list = summaryLine.closest('details')!;
    expect(within(list).getByText('812')).toBeInTheDocument();
    expect(within(list).getByText('Remaining allowance would cross the protected reserve.')).toBeInTheDocument();
    expect(within(list).getByText('Usage is too old to authorize a known allowance.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'History' }));
    expect(screen.queryByText(/Other captains considered/)).not.toBeInTheDocument();
  });
  it('marks an automatic retry in history', async () => {
    vi.mocked(getCaptainSelectionHistory).mockResolvedValue({ success: true, objects: [{ ...decision, id: 'csd_retry', trigger: 'Redispatch' }, { ...decision, id: 'csd_first' }], pageNumber: 1, pageSize: 10, totalPages: 1, totalRecords: 2, totalMs: 1 });
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Compare captains' }));
    fireEvent.click(await screen.findByRole('button', { name: 'History' }));
    const retry = await screen.findByText('Automatic retry');
    expect(retry).toHaveAttribute('title', expect.stringContaining('dispatched this mission again by itself'));
    expect(screen.getAllByText('Automatic retry')).toHaveLength(1);
  });
  it('shows no unlisted-candidate summary when every candidate is listed', async () => {
    mount();
    fireEvent.click(await screen.findByRole('button', { name: 'Compare captains' }));
    await screen.findByRole('table', { name: 'Comparison' });
    expect(screen.queryByText(/Other captains considered/)).not.toBeInTheDocument();
  });
  it('keeps history filters across refresh and resets paging when the filter changes', async () => {
    mount(); await screen.findByText('runtime-model');
    fireEvent.click(screen.getByRole('button', { name: 'Compare captains' }));
    fireEvent.click(screen.getByRole('button', { name: 'History' }));
    await waitFor(() => expect(getCaptainSelectionHistory).toHaveBeenCalled());
    fireEvent.click(screen.getByRole('button', { name: 'Next' }));
    await waitFor(() => expect(getCaptainSelectionHistory).toHaveBeenLastCalledWith('msn_one', expect.objectContaining({ pageNumber: 2 }), expect.any(AbortSignal)));
    fireEvent.change(screen.getByLabelText('Filter selection history by outcome'), { target: { value: 'Waiting' } });
    await waitFor(() => expect(getCaptainSelectionHistory).toHaveBeenLastCalledWith('msn_one', expect.objectContaining({ outcome: 'Waiting', pageNumber: 1 }), expect.any(AbortSignal)));
    fireEvent.click(screen.getAllByRole('button', { name: 'Refresh captain selection' })[0]);
    await waitFor(() => expect(screen.getByLabelText('Filter selection history by outcome')).toHaveValue('Waiting'));
  });
  it('opens historical detail in the same page and passes explicit authorized tenant scope', async () => {
    mount(); await screen.findByText('runtime-model');
    fireEvent.click(screen.getByRole('button', { name: 'Compare captains' }));
    fireEvent.click(screen.getByRole('button', { name: 'History' }));
    await screen.findByRole('button', { name: 'View' });
    fireEvent.click(screen.getByRole('button', { name: 'View' }));
    await waitFor(() => expect(getCaptainSelectionDecision).toHaveBeenLastCalledWith('csd_old', 'tenant', 1, 10, expect.any(AbortSignal)));
  });
  it('labels missing history and unknown reported model without inventing evidence', async () => {
    vi.mocked(getCaptainSelection).mockResolvedValue({ ...summary, historyAvailable: false, currentAttempt: null, latestDecision: null });
    mount('Pending');
    await screen.findByText('Selection history was not recorded for this attempt.');
    expect(screen.queryByText('configured-model')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Preview current selection' })).toBeInTheDocument();
  });
  it('uses the read-only preview contract and clears sensitive cached content on lost permission', async () => {
    mount('Pending'); await screen.findByText('runtime-model');
    fireEvent.click(screen.getByRole('button', { name: 'Preview current selection' }));
    await waitFor(() => expect(previewCaptainSelection).toHaveBeenCalledWith('msn_one', 1, 10, expect.any(AbortSignal)));
    expect(getCaptainSelectionDecision).not.toHaveBeenCalled();
    vi.mocked(getCaptainSelection).mockRejectedValue(new ApiError('Forbidden', 403, null));
    fireEvent.click(screen.getAllByRole('button', { name: 'Refresh captain selection' })[0]);
    await screen.findByRole('alert');
    expect(screen.queryByText('runtime-model')).not.toBeInTheDocument();
    expect(screen.queryByText('configured-model')).not.toBeInTheDocument();
  });
  it('aborts obsolete requests and ignores responses for a previous mission', async () => {
    let finish: ((value: CaptainSelectionSummary) => void) | undefined;
    vi.mocked(getCaptainSelection).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
    const ui = mount();
    const signal = vi.mocked(getCaptainSelection).mock.calls[0][1]!;
    ui.rerender(<MemoryRouter><CaptainSelectionCard missionId="msn_other" status="Pending" /></MemoryRouter>);
    expect(signal.aborted).toBe(true);
    await act(async () => { finish?.({ ...summary, currentAttempt: { ...decision, reportedModel: 'stale-model' } }); });
    expect(screen.queryByText('stale-model')).not.toBeInTheDocument();
  });
});
