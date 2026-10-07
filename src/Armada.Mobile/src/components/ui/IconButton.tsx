import { Pressable, StyleSheet, View } from 'react-native';
import { MIN_TOUCH } from '../../theme/typography';
import { CountBadge } from './CountBadge';
import { Icon, type IconName } from './Icon';
import type { Palette } from '../../theme/palette';

export interface IconButtonProps {
  icon: IconName;
  /** Required: icon-only buttons need a spoken name. */
  label: string;
  onPress: () => void;
  badge?: number;
  color?: keyof Palette;
  testID?: string;
}

export function IconButton({ icon, label, onPress, badge, color = 'primary', testID }: IconButtonProps) {
  const spoken = badge ? `${label}, ${badge}` : label;
  return (
    <Pressable
      testID={testID}
      accessibilityRole="button"
      accessibilityLabel={spoken}
      onPress={onPress}
      hitSlop={4}
      style={({ pressed }) => [styles.base, { opacity: pressed ? 0.6 : 1 }]}
    >
      <View>
        <Icon name={icon} color={color} size={24} />
        {badge ? <View style={styles.badge}><CountBadge count={badge} /></View> : null}
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  base: { minWidth: MIN_TOUCH, minHeight: MIN_TOUCH, alignItems: 'center', justifyContent: 'center' },
  badge: { position: 'absolute', top: -6, right: -10 },
});
