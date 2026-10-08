import { useCallback, useRef, useState, type ReactNode } from 'react';
import { StyleSheet, Switch, View } from 'react-native';
import { AppText } from '../components/ui';
import { useNotifications } from '../notifications/NotificationContext';
import { useTheme } from '../theme/ThemeContext';
import { MIN_TOUCH, spacing, typography } from '../theme/typography';
import { errorMessage } from './useLiveResource';

/** A label / value line in a detail section (the dashboard's detail grids). Long values wrap; ids can be selected. */
export function InfoRow({ label, value, mono, children, testID }: { label: string; value?: string | number | null; mono?: boolean; children?: ReactNode; testID?: string }) {
  const { colors } = useTheme();
  const shown = value === null || value === undefined || value === '' ? '-' : String(value);
  return (
    <View style={[styles.info, { borderBottomColor: colors.border }]} testID={testID} accessible={!children} accessibilityLabel={children ? undefined : `${label}: ${shown}`}>
      <AppText variant="caption" muted>{label}</AppText>
      {children ?? <AppText selectable style={mono ? typography.mono : undefined}>{shown}</AppText>}
    </View>
  );
}

/** A labelled on/off switch (the dashboard's checkboxes in forms). */
export function SwitchField({ label, value, onChange, hint, disabled, testID }: { label: string; value: boolean; onChange: (v: boolean) => void; hint?: string | null; disabled?: boolean; testID?: string }) {
  const { colors } = useTheme();
  return (
    <View style={styles.switchWrap}>
      <View style={styles.switchRow}>
        <AppText variant="label" style={styles.flex}>{label}</AppText>
        <Switch
          testID={testID}
          accessibilityLabel={label}
          accessibilityHint={hint ?? undefined}
          value={value}
          onValueChange={onChange}
          disabled={disabled}
          trackColor={{ true: colors.primary, false: colors.control }}
        />
      </View>
      {hint ? <AppText variant="caption" muted>{hint}</AppText> : null}
    </View>
  );
}

/** A wrapping row of buttons (detail page actions). */
export function ActionRow({ children }: { children: ReactNode }) {
  return <View style={styles.actions}>{children}</View>;
}

export interface ActionRunner {
  /** Key of the action in flight (for busy buttons), or null. */
  busy: string | null;
  /**
   * Run a server action: marks it busy, shows `success` as a toast when given, and shows the server's message as an
   * error toast on failure (never branching on it). Resolves to the action's result, or undefined when it failed.
   */
  run: <R>(key: string, action: () => Promise<R>, success?: string) => Promise<R | undefined>;
}

/** Busy state and toasts for the buttons on a screen (one action at a time). */
export function useActionRunner(): ActionRunner {
  const { pushToast } = useNotifications();
  const [busy, setBusy] = useState<string | null>(null);
  const busyRef = useRef<string | null>(null);
  const run = useCallback(async <R,>(key: string, action: () => Promise<R>, success?: string): Promise<R | undefined> => {
    if (busyRef.current) return undefined;
    busyRef.current = key;
    setBusy(key);
    try {
      const result = await action();
      if (success) pushToast('success', success);
      return result;
    } catch (e) {
      pushToast('error', errorMessage(e));
      return undefined;
    } finally {
      busyRef.current = null;
      setBusy(null);
    }
  }, [pushToast]);
  return { busy, run };
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  info: { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, gap: 2, borderBottomWidth: StyleSheet.hairlineWidth },
  switchWrap: { gap: spacing.xs, marginBottom: spacing.lg },
  switchRow: { flexDirection: 'row', alignItems: 'center', minHeight: MIN_TOUCH, gap: spacing.md },
  actions: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, paddingHorizontal: spacing.lg, marginBottom: spacing.lg },
});
