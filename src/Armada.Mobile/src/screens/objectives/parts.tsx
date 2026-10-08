import { useState, type ReactNode } from 'react';
import { Pressable, ScrollView, StyleSheet, View } from 'react-native';
import { AppText, BottomSheet, Icon } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { CHROME_MAX_FONT_SCALE, MIN_TOUCH, radius, spacing, typography } from '../../theme/typography';

export interface PillOption<K extends string> {
  key: K;
  label: string;
  count?: number;
  description?: string;
}

/** A horizontally scrolling row of toggle pills (the dashboard's backlog group pills). */
export function PillRow<K extends string>({ options, value, onChange, label, testID }: { options: PillOption<K>[]; value: K; onChange: (key: K) => void; label: string; testID?: string }) {
  const { colors } = useTheme();
  return (
    <ScrollView horizontal showsHorizontalScrollIndicator={false} accessibilityLabel={label} contentContainerStyle={styles.pillRow} testID={testID}>
      {options.map((o) => {
        const selected = o.key === value;
        return (
          <Pressable
            key={o.key}
            testID={testID ? `${testID}-${o.key}` : undefined}
            accessibilityRole="button"
            accessibilityLabel={o.count === undefined ? o.label : `${o.label}, ${o.count}`}
            accessibilityHint={o.description}
            accessibilityState={{ selected }}
            onPress={() => onChange(o.key)}
            style={[styles.pill, { borderColor: selected ? colors.primary : colors.border, backgroundColor: selected ? colors.primary : colors.surface }]}
          >
            <AppText variant="caption" color={selected ? 'primaryText' : 'text'} maxFontSizeMultiplier={CHROME_MAX_FONT_SCALE}>{o.label}</AppText>
            {o.count !== undefined ? (
              <AppText variant="caption" color={selected ? 'primaryText' : 'textMuted'} style={styles.bold} maxFontSizeMultiplier={CHROME_MAX_FONT_SCALE}>{String(o.count)}</AppText>
            ) : null}
          </Pressable>
        );
      })}
    </ScrollView>
  );
}

/** Small read-only tags (kind, priority, effort, backlog state). */
export function Tags({ items }: { items: string[] }) {
  const { colors } = useTheme();
  return (
    <View style={styles.tags}>
      {items.filter(Boolean).map((item, i) => (
        <View key={`${item}-${i}`} style={[styles.tag, { borderColor: colors.border, backgroundColor: colors.surfaceRaised }]}>
          <AppText variant="caption" maxFontSizeMultiplier={CHROME_MAX_FONT_SCALE}>{item}</AppText>
        </View>
      ))}
    </View>
  );
}

/** A labelled value tile (the dashboard's info cards and overview counts). */
export function Stat({ label, value }: { label: string; value: string | number }) {
  const { colors } = useTheme();
  return (
    <View style={[styles.stat, { backgroundColor: colors.surface, borderColor: colors.border }]} accessible accessibilityLabel={`${label}: ${value}`}>
      <AppText variant="caption" muted>{label}</AppText>
      <AppText variant="label">{String(value)}</AppText>
    </View>
  );
}

export function StatGrid({ children }: { children: ReactNode }) {
  return <View style={styles.stats}>{children}</View>;
}

export interface MultiOption {
  value: string;
  label: string;
}

/** A multi-choice field (the dashboard's `<select multiple>`): shows the chosen labels, edits them in a sheet of checkboxes. */
export function MultiSelectField({ label, values, options, onChange, disabled, hint, testID }: { label: string; values: string[]; options: MultiOption[]; onChange: (values: string[]) => void; disabled?: boolean; hint?: string | null; testID?: string }) {
  const { colors } = useTheme();
  const { t } = useLocale();
  const [open, setOpen] = useState(false);
  const shown = values.map((v) => options.find((o) => o.value === v)?.label ?? v).join(', ');
  const toggle = (value: string) => onChange(values.includes(value) ? values.filter((v) => v !== value) : [...values, value]);
  return (
    <View style={styles.field}>
      <AppText variant="label">{label}</AppText>
      <Pressable
        testID={testID}
        accessibilityRole="button"
        accessibilityLabel={label}
        accessibilityValue={{ text: shown || t('None') }}
        accessibilityState={{ disabled: !!disabled }}
        disabled={disabled}
        onPress={() => setOpen(true)}
        style={[styles.box, { borderColor: colors.border, backgroundColor: colors.surface, opacity: disabled ? 0.55 : 1 }]}
      >
        <AppText style={styles.flex} muted={!shown} numberOfLines={2}>{shown || t('None')}</AppText>
        <Icon name="chevron-down" size={18} color="textMuted" />
      </Pressable>
      {hint ? <AppText variant="caption" muted>{hint}</AppText> : null}
      <BottomSheet open={open} title={label} onClose={() => setOpen(false)} closeLabel={t('Done')} testID={testID ? `${testID}-sheet` : undefined}>
        {options.length === 0 ? <AppText muted>{t('None')}</AppText> : null}
        {options.map((o) => {
          const checked = values.includes(o.value);
          return (
            <Pressable
              key={o.value}
              accessibilityRole="checkbox"
              accessibilityLabel={o.label}
              accessibilityState={{ checked }}
              onPress={() => toggle(o.value)}
              style={[styles.option, { borderBottomColor: colors.border }]}
            >
              <Icon name={checked ? 'checkbox' : 'square-outline'} color={checked ? 'primary' : 'textMuted'} />
              <AppText style={styles.flex}>{o.label}</AppText>
            </Pressable>
          );
        })}
      </BottomSheet>
    </View>
  );
}

/** The raw record as JSON in a sheet (the dashboard's View JSON). */
export function JsonSheet({ open, title, data, onClose }: { open: boolean; title: string; data: unknown; onClose: () => void }) {
  const { t } = useLocale();
  return (
    <BottomSheet open={open} title={title} onClose={onClose} closeLabel={t('Close')} testID="objective-json">
      <AppText selectable style={typography.mono}>{open ? JSON.stringify(data, null, 2) : ''}</AppText>
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  bold: { fontWeight: '700' },
  pillRow: { gap: spacing.sm, paddingHorizontal: spacing.md, paddingBottom: spacing.md },
  pill: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs, minHeight: MIN_TOUCH - 8, borderWidth: 1, borderRadius: radius.pill, paddingHorizontal: spacing.md },
  tags: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs },
  tag: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.sm, paddingHorizontal: spacing.sm, paddingVertical: 1 },
  stats: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, marginHorizontal: spacing.md, marginBottom: spacing.lg },
  stat: { flexGrow: 1, minWidth: 130, borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, padding: spacing.md, gap: 2 },
  field: { gap: spacing.xs, marginBottom: spacing.lg },
  box: { flexDirection: 'row', alignItems: 'center', borderWidth: 1.5, borderRadius: radius.md, minHeight: MIN_TOUCH, paddingHorizontal: spacing.md, gap: spacing.sm },
  option: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, minHeight: MIN_TOUCH, borderBottomWidth: StyleSheet.hairlineWidth },
});
