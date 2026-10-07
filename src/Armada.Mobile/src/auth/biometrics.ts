import * as LocalAuthentication from 'expo-local-authentication';

/** Whether the device can do biometric (or device passcode) authentication right now. */
export async function biometricsAvailable(): Promise<boolean> {
  try {
    const [hardware, enrolled] = await Promise.all([
      LocalAuthentication.hasHardwareAsync(),
      LocalAuthentication.isEnrolledAsync(),
    ]);
    return hardware && enrolled;
  } catch {
    return false;
  }
}

/** Ask the OS to verify the user. Resolves true only on success; cancel and errors resolve false. */
export async function authenticateBiometric(promptMessage: string, cancelLabel: string): Promise<boolean> {
  try {
    const result = await LocalAuthentication.authenticateAsync({
      promptMessage,
      cancelLabel,
      disableDeviceFallback: false,
    });
    return result.success;
  } catch {
    return false;
  }
}
