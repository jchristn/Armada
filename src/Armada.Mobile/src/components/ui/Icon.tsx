import Ionicons from '@expo/vector-icons/Ionicons';
import type { ComponentProps } from 'react';
import { useTheme } from '../../theme/ThemeContext';
import type { Palette } from '../../theme/palette';

export type IconName = ComponentProps<typeof Ionicons>['name'];

export interface IconProps {
  name: IconName;
  size?: number;
  color?: keyof Palette;
  /** Decorative by default; pass a label only when the icon alone carries meaning. */
  accessibilityLabel?: string;
}

export function Icon({ name, size = 22, color = 'text', accessibilityLabel }: IconProps) {
  const { colors } = useTheme();
  return (
    <Ionicons
      name={name}
      size={size}
      color={colors[color]}
      accessible={!!accessibilityLabel}
      accessibilityLabel={accessibilityLabel}
      importantForAccessibility={accessibilityLabel ? 'yes' : 'no-hide-descendants'}
    />
  );
}
