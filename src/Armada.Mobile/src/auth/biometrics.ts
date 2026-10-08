import { useEffect, useState } from 'react';
import { Platform } from 'react-native';
import * as LocalAuthentication from 'expo-local-authentication';
import { canSaveCredentials } from './savedCredentials';

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

/** What the device calls its biometric method, for labels ("Sign in with Face ID"). */
export type BiometricKind = 'faceId' | 'touchId' | 'fingerprint' | 'biometrics';

export interface BiometricSupport {
  /** Hardware present and enrolled (Face ID / Touch ID / fingerprint set up). */
  enrolled: boolean;
  /** Enrolled, and the keychain / keystore can store an item only biometrics can read (saved passwords). */
  canSavePassword: boolean;
  kind: BiometricKind;
}

/** The label kind for the authentication types the device reports. */
export function biometricKindFor(types: LocalAuthentication.AuthenticationType[], os: string = Platform.OS): BiometricKind {
  const face = types.includes(LocalAuthentication.AuthenticationType.FACIAL_RECOGNITION);
  const finger = types.includes(LocalAuthentication.AuthenticationType.FINGERPRINT);
  if (os === 'ios') {
    if (face) return 'faceId';
    if (finger) return 'touchId';
    return 'biometrics';
  }
  return finger && !face && !types.includes(LocalAuthentication.AuthenticationType.IRIS) ? 'fingerprint' : 'biometrics';
}

/** Probe biometric support; never throws. */
export async function biometricSupport(): Promise<BiometricSupport> {
  const enrolled = await biometricsAvailable();
  let types: LocalAuthentication.AuthenticationType[] = [];
  try {
    types = await LocalAuthentication.supportedAuthenticationTypesAsync();
  } catch {
    types = [];
  }
  return { enrolled, canSavePassword: enrolled && canSaveCredentials(), kind: biometricKindFor(types) };
}

/** The method's name as shown in labels: product names stay untranslated, generic names go through `t`. */
export function biometricName(kind: BiometricKind, t: (text: string) => string): string {
  switch (kind) {
    case 'faceId': return 'Face ID';
    case 'touchId': return 'Touch ID';
    case 'fingerprint': return t('fingerprint');
    default: return t('biometrics');
  }
}

/** Biometric support, probed once on mount; null until known. */
export function useBiometricSupport(): BiometricSupport | null {
  const [support, setSupport] = useState<BiometricSupport | null>(null);
  useEffect(() => {
    let cancelled = false;
    void biometricSupport().then((s) => { if (!cancelled) setSupport(s); });
    return () => { cancelled = true; };
  }, []);
  return support;
}
