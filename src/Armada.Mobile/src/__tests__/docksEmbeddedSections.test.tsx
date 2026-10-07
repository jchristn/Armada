import { fireEvent, screen } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import { EMBEDDED_SECTIONS, embeddedSectionFor } from '../screens/embeddedSections';
import { RoutePlaceholder } from '../screens/RoutePlaceholder';
import { page } from '../test/operationsClient';
import { mockRouter, setMockParams } from '../test/routerMock';
import { renderScreen } from '../test/screen';

jest.mock('@dashboard/api/client', () => require('../test/operationsClient').operationsClientMockFactory());
jest.mock('expo-router', () => require('../test/routerMock').routerMockFactory());

const mockWindow = { width: 390, height: 844 };
jest.mock('react-native/Libraries/Utilities/useWindowDimensions', () => ({
  __esModule: true,
  default: () => ({ width: mockWindow.width, height: mockWindow.height, scale: 2, fontScale: 1 }),
}));

const api = client as jest.Mocked<typeof client>;
const realError = console.error;
beforeAll(() => {
  jest.spyOn(console, 'error').mockImplementation((...args: unknown[]) => {
    if (typeof args[0] === 'string' && args[0].includes('[react-native-gesture-handler]')) return;
    realError(...args);
  });
});
afterAll(() => { (console.error as jest.Mock).mockRestore(); });

beforeEach(() => {
  mockWindow.width = 390;
  setMockParams({});
  api.listDocks.mockResolvedValue(page([{ id: 'dck_1', vesselId: 'vsl_1', captainId: null, branchName: 'armada/x', worktreePath: '/w', active: true, createdUtc: '2026-10-07T10:00:00Z' }]) as never);
  api.listSignals.mockResolvedValue(page([{ id: 'sig_1', type: 'Mail', payload: 'hi', fromCaptainId: null, toCaptainId: null, read: false, createdUtc: '2026-10-07T10:00:00Z' }]) as never);
  api.listEvents.mockResolvedValue(page([]) as never);
  api.listVessels.mockResolvedValue(page([]) as never);
  api.listCaptains.mockResolvedValue(page([]) as never);
  api.getSignal.mockResolvedValue({ id: 'sig_1', type: 'Mail', payload: 'hi', fromCaptainId: null, toCaptainId: null, read: false, createdUtc: '2026-10-07T10:00:00Z', tenantId: null });
});

describe('placeholder hubs serve the sections already built', () => {
  it('maps hub queries to sections', () => {
    expect(embeddedSectionFor('/captains', { tab: 'docks' })?.key).toBe('docks');
    expect(embeddedSectionFor('/activity', { source: ['signals'] })?.key).toBe('signals');
    expect(embeddedSectionFor('/activity', { source: 'history' })).toBeNull();
    expect(embeddedSectionFor('/vessels', { tab: 'docks' })).toBeNull();
    expect(Object.keys(EMBEDDED_SECTIONS['/activity'].sections)).toEqual(['events', 'signals']);
  });

  it('/captains?tab=docks renders the Docks list; a row pushes the dock on phones', async () => {
    setMockParams({ tab: 'docks' });
    await renderScreen(<RoutePlaceholder pattern="/captains" />);
    expect(await screen.findByTestId('embedded-section-docks')).toBeTruthy();
    expect(screen.getByTestId('embedded-section-note')).toHaveTextContent('Docks in Captains; the rest of Captains is coming in W3.2');
    await fireEvent.press(await screen.findByTestId('dock-row-dck_1'));
    expect(mockRouter.push).toHaveBeenCalledWith('/docks/dck_1');
    await fireEvent.press(screen.getByTestId('embedded-section-back'));
    expect(mockRouter.setParams).toHaveBeenCalledWith({ tab: '' });
  });

  it('on tablets the detail opens beside the list', async () => {
    mockWindow.width = 1180;
    setMockParams({ source: 'signals' });
    await renderScreen(<RoutePlaceholder pattern="/activity" />);
    await fireEvent.press(await screen.findByTestId('signal-row-sig_1'));
    expect(await screen.findByTestId('signal-detail')).toBeTruthy();
    expect(screen.getByTestId('split-view')).toBeTruthy();
    expect(mockRouter.push).not.toHaveBeenCalledWith('/signals/sig_1');
  });

  it('without a section the hub placeholder lists the built sections', async () => {
    await renderScreen(<RoutePlaceholder pattern="/activity" />);
    expect(await screen.findByTestId('route-placeholder-title')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('route-placeholder-section-events'));
    expect(mockRouter.setParams).toHaveBeenCalledWith({ source: 'events' });
  });

  it('other placeholders are unchanged', async () => {
    setMockParams({ tab: 'docks' });
    await renderScreen(<RoutePlaceholder pattern="/vessels" />);
    expect(await screen.findByTestId('route-placeholder-workstream')).toHaveTextContent('Coming in W3.1');
    expect(screen.queryByTestId('route-placeholder-section-docks')).toBeNull();
  });
});
