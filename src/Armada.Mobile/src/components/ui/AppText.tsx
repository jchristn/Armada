import type { Ref } from 'react';
import { Text, type TextProps } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { typography, type TypographyVariant } from '../../theme/typography';
import type { Palette } from '../../theme/palette';

export interface AppTextProps extends TextProps {
  variant?: TypographyVariant;
  /** A palette color name; defaults to `text` (or `textMuted` when `muted`). */
  color?: keyof Palette;
  muted?: boolean;
  /** The native text (for moving screen-reader focus to it). */
  ref?: Ref<Text>;
}

/** Text in the app's type scale and theme. Font scaling stays on (Dynamic Type / Android font size). */
export function AppText({ variant = 'body', color, muted, style, ...rest }: AppTextProps) {
  const { colors } = useTheme();
  const tint = colors[color ?? (muted ? 'textMuted' : 'text')];
  return <Text {...rest} style={[typography[variant], { color: tint }, style]} />;
}
