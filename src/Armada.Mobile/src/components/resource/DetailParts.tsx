import type { ReactNode } from 'react';
import { Pressable, RefreshControl, ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import { AppText } from '../ui/AppText';
import { BottomSheet } from '../ui/BottomSheet';
import { Icon } from '../ui/Icon';
import { ErrorState, LoadingState } from '../ui/States';

export interface DetailBodyProps {
  children: ReactNode;
  refreshing?: boolean;
  onRefresh?: () => void;
  /** Inside a tablet split view (no safe-area padding; the list pane owns the screen edges). */
  embedded?: boolean;
  testID?: string;
}

/** The scrolling body of a detail screen or a split-view detail pane, with pull to refresh. */
export function DetailBody({ children, refreshing = false, onRefresh, embedded, testID }: DetailBodyProps) {
  const { colors } = useTheme();
  const scroll = (
    <ScrollView
      testID={testID}
      contentContainerStyle={styles.content}
      keyboardShouldPersistTaps="handled"
      refreshControl={onRefresh ? <RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={colors.primary} /> : undefined}
    >
      <View style={styles.column}>{children}</View>
    </ScrollView>
  );
  if (embedded) return <View style={[styles.fill, { backgroundColor: colors.background }]}>{scroll}</View>;
  return <SafeAreaView edges={['left', 'right']} style={[styles.fill, { backgroundColor: colors.background }]}>{scroll}</SafeAreaView>;
}

/** Loading and error states of a detail screen before its data arrives. */
export function DetailPending({ loading, error, onRetry }: { loading: boolean; error: string; onRetry: () => void }) {
  const { t } = useLocale();
  if (loading) return <LoadingState label={t('Loading...')} />;
  return <ErrorState title={t('Something went wrong')} message={error || t('Not found.')} retryLabel={t('Retry')} onRetry={onRetry} />;
}

/** Title block of a detail screen: name, badges, and a muted subtitle (usually the id). */
export function DetailHeader({ title, subtitle, badges, testID }: { title: string; subtitle?: string | null; badges?: ReactNode; testID?: string }) {
  return (
    <View style={styles.header}>
      <AppText variant="title" accessibilityRole="header" testID={testID}>{title}</AppText>
      {badges ? <View style={styles.badges}>{badges}</View> : null}
      {subtitle ? <AppText variant="mono" muted selectable>{subtitle}</AppText> : null}
    </View>
  );
}

export interface FieldProps {
  label: string;
  value: ReactNode;
  mono?: boolean;
  /** Makes the value a link (for example to the related vessel). */
  onPress?: () => void;
  testID?: string;
}

/** One labelled value in a detail card; text values are selectable so they can be copied. */
export function Field({ label, value, mono, onPress, testID }: FieldProps) {
  const { colors } = useTheme();
  const empty = value === null || value === undefined || value === '';
  const body = typeof value === 'string' || typeof value === 'number' || empty ? (
    <AppText variant={mono ? 'mono' : 'body'} color={onPress ? 'primary' : undefined} muted={empty} selectable={!onPress}>{empty ? '-' : String(value)}</AppText>
  ) : value;
  const inner = (
    <View style={styles.fieldText}>
      <AppText variant="caption" muted>{label}</AppText>
      {body}
    </View>
  );
  if (onPress && !empty) {
    return (
      <Pressable testID={testID} onPress={onPress} accessibilityRole="link" accessibilityLabel={`${label}: ${typeof value === 'string' ? value : ''}`} style={[styles.field, { borderBottomColor: colors.border }]}>
        {inner}
        <Icon name="chevron-forward" size={16} color="textMuted" />
      </Pressable>
    );
  }
  return <View testID={testID} style={[styles.field, { borderBottomColor: colors.border }]}>{inner}</View>;
}

/** A titled card of fields. */
export function FieldCard({ title, children, testID }: { title?: string; children: ReactNode; testID?: string }) {
  const { colors } = useTheme();
  return (
    <View style={styles.cardWrap} testID={testID}>
      {title ? <AppText variant="subheading" muted accessibilityRole="header" style={styles.cardTitle}>{title}</AppText> : null}
      <View style={[styles.card, { backgroundColor: colors.surface, borderColor: colors.border }]}>{children}</View>
    </View>
  );
}

/** A block of long text (instructions, notes, logs), selectable; monospace for code and output. */
export function TextBlock({ title, text, mono, emptyText, testID }: { title: string; text: string | null | undefined; mono?: boolean; emptyText?: string; testID?: string }) {
  const { colors } = useTheme();
  return (
    <View style={styles.cardWrap} testID={testID}>
      <AppText variant="subheading" muted accessibilityRole="header" style={styles.cardTitle}>{title}</AppText>
      <View style={[styles.card, styles.textCard, { backgroundColor: colors.surface, borderColor: colors.border }]}>
        <AppText variant={mono ? 'mono' : 'body'} muted={!text} selectable>{text || emptyText || '-'}</AppText>
      </View>
    </View>
  );
}

/** A wrapping row of action buttons under a detail header. */
export function ActionBar({ children }: { children: ReactNode }) {
  return <View style={styles.actions}>{children}</View>;
}

/** The dashboard's "View JSON": the raw record, pretty-printed and selectable. */
export function JsonSheet({ open, title, data, onClose }: { open: boolean; title: string; data: unknown; onClose: () => void }) {
  const { t } = useLocale();
  let text = '';
  try { text = JSON.stringify(data, null, 2) ?? ''; } catch { text = String(data); }
  return (
    <BottomSheet open={open} title={title} onClose={onClose} closeLabel={t('Close')} testID="json-sheet">
      <AppText variant="mono" selectable>{text}</AppText>
    </BottomSheet>
  );
}

export const detailStyles = StyleSheet.create({
  inlineButton: { marginBottom: 0 },
});

const styles = StyleSheet.create({
  fill: { flex: 1 },
  content: { paddingVertical: spacing.lg, flexGrow: 1 },
  column: { width: '100%', maxWidth: 820, alignSelf: 'center' },
  header: { marginHorizontal: spacing.lg, marginBottom: spacing.lg, gap: spacing.xs },
  badges: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  field: { flexDirection: 'row', alignItems: 'center', paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, borderBottomWidth: StyleSheet.hairlineWidth, gap: spacing.sm },
  fieldText: { flex: 1, gap: 2 },
  cardWrap: { marginBottom: spacing.xl },
  cardTitle: { marginHorizontal: spacing.lg, marginBottom: spacing.sm, textTransform: 'uppercase' },
  card: { borderRadius: radius.md, borderWidth: StyleSheet.hairlineWidth, overflow: 'hidden', marginHorizontal: spacing.md },
  textCard: { padding: spacing.md },
  actions: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, marginHorizontal: spacing.md, marginBottom: spacing.lg },
});
