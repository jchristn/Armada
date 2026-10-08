import { useState } from 'react';
import { Pressable, ScrollView, StyleSheet, View } from 'react-native';
import type { AskTrackedWork } from '@dashboard/types/models';
import { entityTypeLabel, isWorkActive } from '@dashboard/lib/askWork';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, radius, spacing } from '../../theme/typography';
import { AppText } from '../ui/AppText';
import { Icon } from '../ui/Icon';
import { StatusBadge } from '../ui/StatusBadge';
import { statusTone } from './statusTone';

/** Collapsible "Work in this conversation" strip (the dashboard's AskWorkStrip): every tracked item with a live status chip. */
export function WorkStrip({ work, onSelect }: { work: AskTrackedWork[]; onSelect: (work: AskTrackedWork) => void }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const [open, setOpen] = useState(true);
  if (work.length === 0) return null;
  const activeCount = work.filter((w) => isWorkActive(w)).length;
  const countText = activeCount > 0
    ? t('{{active}} active of {{total}}', { active: activeCount, total: work.length })
    : t('{{total}} finished', { total: work.length });

  return (
    <View style={[styles.strip, { borderBottomColor: colors.border, backgroundColor: colors.surface }]} testID="ask-work-strip">
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={`${t('Work in this conversation')}, ${countText}`}
        accessibilityState={{ expanded: open }}
        onPress={() => setOpen((v) => !v)}
        style={styles.toggle}
      >
        <Icon name={open ? 'chevron-down' : 'chevron-forward'} size={16} color="textMuted" />
        <AppText variant="label">{t('Work in this conversation')}</AppText>
        <AppText variant="caption" muted>{countText}</AppText>
      </Pressable>
      {open ? (
        <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.items}>
          {work.map((item) => (
            <Pressable
              key={item.id}
              testID={`work-chip-${item.id}`}
              accessibilityRole="button"
              accessibilityLabel={t('Show the live card for {{title}}', { title: item.title || item.entityId })}
              accessibilityValue={{ text: [entityTypeLabel(t, item.entityType), item.status ? t(item.status) : null].filter(Boolean).join(', ') }}
              onPress={() => onSelect(item)}
              style={[styles.chip, { borderColor: isWorkActive(item) ? colors.info : colors.border }]}
            >
              <AppText variant="caption" muted>{entityTypeLabel(t, item.entityType)}</AppText>
              <AppText variant="caption" numberOfLines={1} style={styles.title}>{item.title || item.entityId}</AppText>
              {item.status ? <StatusBadge label={t(item.status)} tone={statusTone(item.status)} /> : null}
            </Pressable>
          ))}
        </ScrollView>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  strip: { borderBottomWidth: StyleSheet.hairlineWidth },
  toggle: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, paddingHorizontal: spacing.md, minHeight: MIN_TOUCH - 8 },
  items: { gap: spacing.sm, paddingHorizontal: spacing.md, paddingBottom: spacing.sm },
  chip: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs, borderWidth: 1, borderRadius: radius.pill, paddingHorizontal: spacing.md, minHeight: MIN_TOUCH - 8, maxWidth: 320 },
  title: { flexShrink: 1 },
});
