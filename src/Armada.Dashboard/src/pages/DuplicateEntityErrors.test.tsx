import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReactElement } from 'react';
import Fleets from './Fleets';
import Personas from './Personas';
import Pipelines from './Pipelines';
import VesselFormModal from '../components/vessels/VesselFormModal';
import {
  ApiError, NetworkError, createFleet, createPersona, createPipeline, createVessel, listFleets, listPersonas,
  listPipelines, listPromptTemplates, listVessels,
} from '../api/client';
import { translateTemplate } from '../i18n/runtime';

vi.mock('../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/client')>();
  return {
    ...actual,
    listFleets: vi.fn(), listVessels: vi.fn(), listPipelines: vi.fn(), listPersonas: vi.fn(), listPromptTemplates: vi.fn(),
    createFleet: vi.fn(), createPersona: vi.fn(), createPipeline: vi.fn(), createVessel: vi.fn(),
  };
});

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));
vi.mock('../context/AuthContext', () => ({ useAuth: () => ({ isTenantAdmin: true, isAdmin: true, user: { user: { id: 'usr_1', tenantId: 'default' } } }) }));
vi.mock('../components/shared/UserScopeFilter', () => ({ default: () => null }));

function page<T>(objects: T[]) {
  return { success: true, pageNumber: 1, pageSize: 25, totalPages: 1, totalRecords: objects.length, totalMs: 1, objects } as never;
}

/** The 409 the server returns for a duplicate name: Conflict, its message, and a DuplicateEntity detail in Data. */
function duplicate(message: string, entityType: string): ApiError {
  return new ApiError(message, 409, { code: 'DuplicateEntity', entityType, field: 'Name', value: 'core' });
}

beforeEach(() => {
  localStorage.clear();
  vi.mocked(listFleets).mockResolvedValue(page([]));
  vi.mocked(listVessels).mockResolvedValue(page([]));
  vi.mocked(listPipelines).mockResolvedValue(page([]));
  vi.mocked(listPersonas).mockResolvedValue(page([]));
  vi.mocked(listPromptTemplates).mockResolvedValue(page([]));
});

async function createThrough(element: ReactElement, addLabel: string, list: () => unknown) {
  render(<MemoryRouter>{element}</MemoryRouter>);
  await waitFor(() => expect(list).toHaveBeenCalled());
  fireEvent.click(screen.getByRole('button', { name: addLabel }));
  const name = await screen.findByRole('textbox', { name: 'Name' });
  fireEvent.change(name, { target: { value: 'core' } });
  fireEvent.submit(name.closest('form') as HTMLFormElement);
  return screen.findByRole('alertdialog');
}

describe('create forms show the server message for a duplicate entity', () => {
  it('Fleets shows the 409 message instead of a generic failure', async () => {
    vi.mocked(createFleet).mockRejectedValue(duplicate('A fleet named core already exists', 'Fleet'));
    const dialog = await createThrough(<Fleets />, '+ Fleet', listFleets);
    expect(dialog).toHaveTextContent('A fleet named core already exists');
    expect(dialog).not.toHaveTextContent('Save failed.');
  });

  it('Fleets falls back to the generic message when no response arrived', async () => {
    vi.mocked(createFleet).mockRejectedValue(new NetworkError('Failed to fetch', new TypeError('Failed to fetch')));
    const dialog = await createThrough(<Fleets />, '+ Fleet', listFleets);
    expect(dialog).toHaveTextContent('Save failed.');
  });

  it('Personas shows the 409 message', async () => {
    vi.mocked(createPersona).mockRejectedValue(duplicate('A persona named core already exists', 'Persona'));
    const dialog = await createThrough(<Personas />, '+ Persona', listPersonas);
    expect(dialog).toHaveTextContent('A persona named core already exists');
  });

  it('Pipelines shows the 409 message', async () => {
    vi.mocked(createPipeline).mockRejectedValue(duplicate('A pipeline named core already exists', 'Pipeline'));
    const dialog = await createThrough(<Pipelines />, '+ Pipeline', listPipelines);
    expect(dialog).toHaveTextContent('A pipeline named core already exists');
  });

  it('VesselFormModal reports the 409 message through onError', async () => {
    vi.mocked(createVessel).mockRejectedValue(duplicate('A vessel named core already exists in this fleet', 'Vessel'));
    const onError = vi.fn();
    render(<VesselFormModal vessel={null} fleets={[]} pipelines={[]} onClose={vi.fn()} onSaved={vi.fn()} onError={onError} />);
    fireEvent.submit(screen.getByRole('form', { name: 'Create Vessel' }));
    await waitFor(() => expect(onError).toHaveBeenCalledWith('A vessel named core already exists in this fleet'));
  });
});
