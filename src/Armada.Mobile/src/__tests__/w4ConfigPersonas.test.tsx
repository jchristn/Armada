import { act, fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { Persona, PromptTemplate } from '@dashboard/types/models';
import MoreLayout from '../app/(app)/(more)/_layout';
import ConfigurationRoute from '../app/(app)/(more)/configuration';
import PersonaRoute from '../app/(app)/(more)/personas/[name]';
import { page, renderW4Routes, resetW4 } from '../test/w4';

jest.mock('@dashboard/api/client', () => require('../test/w4Client').autoMockClient());

const api = client as jest.Mocked<typeof client>;

function persona(over: Partial<Persona> = {}): Persona {
  return {
    id: 'prs_1', tenantId: 'ten_1', userId: 'usr_1', scope: 'TenantWide', name: 'Architect', description: 'Designs', promptTemplateName: 'persona.architect',
    isBuiltIn: true, active: true, defaultCaptainId: null, createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-02T00:00:00Z', ...over,
  };
}

const TEMPLATE: PromptTemplate = {
  id: 'ptp_1', tenantId: 'ten_1', userId: null, scope: 'TenantWide', name: 'persona.architect', description: 'Architect prompt', category: 'persona',
  content: 'You are the architect.', isBuiltIn: true, active: true, createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-01T00:00:00Z',
};

const ROUTES = {
  '(more)/_layout': MoreLayout,
  '(more)/more': () => null,
  '(more)/configuration': ConfigurationRoute,
  '(more)/personas/[name]': PersonaRoute,
};

beforeEach(async () => {
  await resetW4();
  jest.clearAllMocks();
  api.listPersonas.mockResolvedValue(page([persona(), persona({ id: 'prs_2', name: 'Mine', isBuiltIn: false, scope: 'UserSpecific', promptTemplateName: 'persona.worker' })]) as never);
  api.listPromptTemplates.mockResolvedValue(page([TEMPLATE, { ...TEMPLATE, id: 'ptp_2', name: 'persona.worker' }]) as never);
  api.listCaptains.mockResolvedValue(page([{ id: 'cpt_1', name: 'Ada' }]) as never);
  api.listWorkflowProfiles.mockResolvedValue(page([]) as never);
  api.listFleets.mockResolvedValue(page([]) as never);
  api.listVessels.mockResolvedValue(page([]) as never);
});

describe('Configuration > Personas', () => {
  it('lists personas and creates one with the chosen template (personal for regular users)', async () => {
    api.createPersona.mockResolvedValue(persona({ name: 'Reviewer' }));
    await renderW4Routes(ROUTES, '/configuration?tab=personas', 'user');
    await waitFor(() => expect(screen.getByTestId('persona-row-Architect')).toBeTruthy());
    expect(screen.getByTestId('persona-row-Mine')).toBeTruthy();
    // Regular users cannot edit or delete a tenant-wide built-in persona.
    expect(screen.queryByTestId('persona-row-Architect-swipe-edit')).toBeNull();
    await fireEvent.press(screen.getByTestId('personas-create'));
    await waitFor(() => expect(screen.getByTestId('persona-form-name')).toBeTruthy());
    // The resource form sheet keeps Save in its footer, reachable without scrolling a long form.
    expect(within(screen.getByTestId('persona-form-footer')).getByTestId('persona-form-submit')).toBeTruthy();
    await fireEvent.changeText(screen.getByTestId('persona-form-name'), 'Reviewer');
    await act(async () => { await fireEvent.press(screen.getByTestId('persona-form-submit')); });
    expect(screen.getByTestId('persona-form-error')).toHaveTextContent('Prompt Template Name is required.');
    await fireEvent.press(screen.getByTestId('persona-form-promptTemplateName'));
    await fireEvent.press(screen.getByTestId('persona-form-promptTemplateName-option-persona.worker'));
    await act(async () => { await fireEvent.press(screen.getByTestId('persona-form-submit')); });
    expect(api.createPersona).toHaveBeenCalledWith({ name: 'Reviewer', promptTemplateName: 'persona.worker', scope: 'UserSpecific' });
  });

  it('opens a persona and edits its description', async () => {
    api.getPersona.mockResolvedValue(persona());
    api.getPromptTemplate.mockResolvedValue(TEMPLATE);
    api.updatePersona.mockResolvedValue(persona({ description: 'New' }));
    await renderW4Routes(ROUTES, '/personas/Architect');
    await waitFor(() => expect(screen.getByTestId('persona-title')).toHaveTextContent('Architect'));
    expect(screen.getByText('You are the architect.')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('persona-edit'));
    await waitFor(() => expect(screen.getByTestId('persona-form-description')).toBeTruthy());
    await fireEvent.changeText(screen.getByTestId('persona-form-description'), 'New');
    await act(async () => { await fireEvent.press(screen.getByTestId('persona-form-submit')); });
    expect(api.updatePersona).toHaveBeenCalledWith('Architect', { description: 'New', promptTemplateName: 'persona.architect', defaultCaptainId: null });
    expect(api.getPersona).toHaveBeenCalledTimes(2);
  });

  it('resets a built-in backing prompt after confirmation', async () => {
    api.getPersona.mockResolvedValue(persona());
    api.getPromptTemplate.mockResolvedValue(TEMPLATE);
    api.resetPromptTemplate.mockResolvedValue({ ...TEMPLATE, content: 'Default.' });
    await renderW4Routes(ROUTES, '/personas/Architect');
    await waitFor(() => expect(screen.getByText('Reset to Default')).toBeTruthy());
    await fireEvent.press(screen.getByText('Reset to Default'));
    await act(async () => { await fireEvent.press(screen.getByTestId('persona-confirm-confirm')); });
    expect(api.resetPromptTemplate).toHaveBeenCalledWith('persona.architect');
    await waitFor(() => expect(screen.getByText('Default.')).toBeTruthy());
  });
});
