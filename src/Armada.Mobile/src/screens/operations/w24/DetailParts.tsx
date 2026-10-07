import type { ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';
import { AppText, Button, EmptyState, ErrorState, LoadingState } from '../../../components/ui';
import { useLocale } from '../../../i18n/LocaleContext';
import { spacing } from '../../../theme/typography';

/** "2 minutes ago (Oct 7, 2026 13:50)" like the dashboard's detail fields; '-' when there is no time. */
export function useWhen(): (utc: string | null | undefined) => string {
  const { formatRelativeTime, formatDateTime } = useLocale();
  return (utc) => (utc ? `${formatRelativeTime(utc)} (${formatDateTime(utc)})` : '-');
}

/** A row of detail actions (wraps on narrow screens). */
export function DetailActions({ children }: { children: ReactNode }) {
  return <View style={styles.actions}>{children}</View>;
}

/** Loading, failed, or missing states of a detail screen. */
export function DetailState({ loading, error, missing, missingTitle, onRetry, onBack, backLabel }: {
  loading: boolean;
  error: string | null;
  missing: boolean;
  missingTitle: string;
  onRetry: () => void;
  onBack?: () => void;
  backLabel?: string;
}) {
  const { t } = useLocale();
  if (loading) return <LoadingState label={t('Loading...')} />;
  if (error) {
    return (
      <View style={styles.fill}>
        <ErrorState title={t('Something went wrong')} message={error} retryLabel={t('Retry')} onRetry={onRetry} />
        {onBack && backLabel ? <View style={styles.pad}><Button label={backLabel} variant="ghost" icon="arrow-back" onPress={onBack} /></View> : null}
      </View>
    );
  }
  if (missing) return <EmptyState title={missingTitle} />;
  return null;
}

/** A labelled block of preformatted text (test output, payloads). */
export function DetailHeading({ children }: { children: string }) {
  return <AppText variant="subheading" muted accessibilityRole="header" style={styles.heading}>{children}</AppText>;
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  pad: { paddingHorizontal: spacing.lg },
  actions: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, paddingHorizontal: spacing.md, marginBottom: spacing.lg },
  heading: { marginHorizontal: spacing.lg, marginBottom: spacing.sm, textTransform: 'uppercase' },
});
