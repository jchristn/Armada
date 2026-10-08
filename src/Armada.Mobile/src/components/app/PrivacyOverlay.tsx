import { useEffect, useState } from 'react';
import { AppState, Image, Modal, StyleSheet, View, type AppStateStatus } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { MODAL_ORIENTATIONS } from '../ui/modalOrientations';

/** True for the app states in which the OS may take a snapshot of the screen (app switcher, Recents). */
export function hidesContent(state: AppStateStatus): boolean {
  return state === 'inactive' || state === 'background';
}

/**
 * Covers the whole app (including open sheets, since it is a modal of its own) with a plain screen whenever the app
 * is not in the foreground, so the app switcher snapshot (iOS) and the Recents thumbnail (Android) show no
 * conversation, code, diff, log, or one-time secret. iOS takes its snapshot after 'inactive' / 'background', which
 * this covers. Android takes the Recents thumbnail as the activity pauses, which can be before JavaScript runs, so
 * there this is best effort; FLAG_SECURE would be certain but also blocks the user's own screenshots and screen
 * sharing, so it is not set app-wide (docs/MOBILE.md, Security).
 */
export function PrivacyOverlay() {
  const { colors } = useTheme();
  const [hidden, setHidden] = useState(() => hidesContent(AppState.currentState));

  useEffect(() => {
    const sub = AppState.addEventListener('change', (next) => setHidden(hidesContent(next)));
    return () => sub.remove();
  }, []);

  return (
    <Modal supportedOrientations={MODAL_ORIENTATIONS} visible={hidden} animationType="none" transparent={false} statusBarTranslucent onRequestClose={() => undefined}>
      <View testID="privacy-overlay" style={[styles.fill, { backgroundColor: colors.background }]}>
        <Image source={require('../../../assets/images/icon.png')} style={styles.logo} accessible={false} />
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1, alignItems: 'center', justifyContent: 'center' },
  logo: { width: 96, height: 96, borderRadius: 20 },
});
