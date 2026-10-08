import { useEffect, useRef } from 'react';
import { Pressable, ScrollView, StyleSheet, View } from 'react-native';
import type { Captain } from '@dashboard/types/models';
import { useLocale } from '../../i18n/LocaleContext';
import { focusElement } from '../../lib/accessibility';
import { useHardwareBack } from '../../navigation/useHardwareBack';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, radius, spacing } from '../../theme/typography';
import { AppText } from '../ui/AppText';
import { Icon } from '../ui/Icon';
import { captainLabel } from './ConversationOptionsSheet';

export interface CaptainMenuProps {
  /** The conversation's captain ('' for none). */
  value: string;
  captains: Captain[];
  onSelect: (captainId: string) => void;
  onClose: () => void;
}

/** Longest the dropdown grows before its options scroll (about five rows). */
export const CAPTAIN_MENU_MAX_HEIGHT = 5 * (MIN_TOUCH + spacing.sm);

/**
 * The captain picker of Ask as an inline dropdown anchored under the captain bar (the dashboard's captain `<select>`
 * in the conversation header). The options are radio buttons; screen-reader focus moves to the chosen one when the
 * dropdown opens. Choosing a captain, tapping the bar again, tapping outside, Android back, or the iOS escape gesture
 * closes it, and focus returns to the bar (the caller's job: it owns the bar).
 */
export function CaptainMenu({ value, captains, onSelect, onClose }: CaptainMenuProps) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const selectedRef = useRef<View | null>(null);
  const options = [{ value: '', label: t('No captain (quick actions only)') }, ...captains.map((c) => ({ value: c.id, label: captainLabel(c) }))];
  useHardwareBack(true, onClose);

  useEffect(() => { focusElement(selectedRef); }, []);

  return (
    <View
      testID="ask-captain-menu"
      accessibilityRole="radiogroup"
      accessibilityLabel={t('Captain')}
      onAccessibilityEscape={onClose}
      style={[styles.menu, { backgroundColor: colors.surfaceRaised, borderColor: colors.border, shadowColor: colors.text }]}
    >
      <ScrollView style={{ maxHeight: CAPTAIN_MENU_MAX_HEIGHT }} keyboardShouldPersistTaps="handled" bounces={false}>
        {options.map((option, index) => {
          const selected = option.value === value;
          return (
            <Pressable
              key={option.value || '__none'}
              ref={selected ? selectedRef : undefined}
              testID={`ask-captain-menu-option-${option.value || 'none'}`}
              accessibilityRole="radio"
              accessibilityLabel={option.label}
              accessibilityState={{ checked: selected }}
              onPress={() => { onClose(); if (!selected) onSelect(option.value); }}
              style={({ pressed }) => [
                styles.option,
                index > 0 ? { borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: colors.border } : null,
                { backgroundColor: pressed ? colors.background : 'transparent' },
              ]}
            >
              <AppText variant="body" numberOfLines={2} style={styles.label}>{option.label}</AppText>
              {selected ? <Icon name="checkmark" color="primary" size={18} /> : null}
            </Pressable>
          );
        })}
      </ScrollView>
    </View>
  );
}

const styles = StyleSheet.create({
  menu: {
    marginHorizontal: spacing.sm,
    marginTop: spacing.xs,
    borderWidth: StyleSheet.hairlineWidth,
    borderRadius: radius.md,
    elevation: 6,
    shadowOpacity: 0.18,
    shadowRadius: 10,
    shadowOffset: { width: 0, height: 4 },
  },
  option: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, minHeight: MIN_TOUCH, paddingHorizontal: spacing.md, paddingVertical: spacing.sm },
  label: { flex: 1 },
});
