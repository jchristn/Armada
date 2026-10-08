import { act, fireEvent, render, screen } from '@testing-library/react-native';
import { Alert } from 'react-native';
import * as auth from '../auth/AuthContext';
import { HeaderActions } from '../components/app/HeaderActions';
import { NotEncryptedBadge } from '../components/app/NotEncryptedIndicator';
import { NotificationProvider } from '../notifications/NotificationContext';
import { SocketProvider } from '../socket/SocketContext';
import { AppProviders } from '../test/render';

jest.mock('@dashboard/api/client', () => require('../test/mockClient').clientMockFactory());
jest.mock('expo-router', () => ({ useRouter: () => ({ push: jest.fn() }) }));

const realUseAuth = auth.useAuth;

afterEach(() => jest.restoreAllMocks());

function signedInTo(url: string | null, status: auth.AuthState['status'] = 'signedIn') {
  jest.spyOn(auth, 'useAuth').mockImplementation(() => ({
    ...realUseAuth(),
    status,
    activeProfile: url ? { id: 'prf_1', name: 'Admiral', url, kind: 'Direct', biometricUnlock: false } : null,
  } as auth.AuthState));
}

async function renderHeader() {
  await render(
    <AppProviders>
      <SocketProvider serverUrl={null} token={null}>
        <NotificationProvider schedule={() => 0}>
          <HeaderActions />
        </NotificationProvider>
      </SocketProvider>
    </AppProviders>,
  );
}

describe('"Not encrypted" while signed in over http:// (F-45)', () => {
  it('the phone header shows it for an http:// server and explains on tap', async () => {
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
    signedInTo('http://203.0.113.7:7890');
    await renderHeader();
    expect(screen.getByLabelText('Not encrypted')).toBeTruthy();
    await act(async () => { await fireEvent.press(screen.getByTestId('not-encrypted-indicator')); });
    expect(alert).toHaveBeenCalledWith('This connection is not encrypted', expect.stringContaining('plain text over the internet'));
  });

  it('nothing for https', async () => {
    signedInTo('https://admiral.example');
    await renderHeader();
    expect(screen.queryByTestId('not-encrypted-indicator')).toBeNull();
  });

  it('nothing while signed out, even for an http:// profile', async () => {
    signedInTo('http://203.0.113.7:7890', 'signedOut');
    await renderHeader();
    expect(screen.queryByTestId('not-encrypted-indicator')).toBeNull();
  });

  it('the full badge names it, and a LAN server gets the LAN wording', async () => {
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
    await render(<AppProviders><NotEncryptedBadge url="http://192.168.1.20:7890" /></AppProviders>);
    expect(screen.getByText('Not encrypted')).toBeTruthy();
    await act(async () => { await fireEvent.press(screen.getByTestId('not-encrypted-indicator')); });
    expect(alert).toHaveBeenCalledWith('This connection is not encrypted', expect.stringContaining('network you trust'));
  });
});
