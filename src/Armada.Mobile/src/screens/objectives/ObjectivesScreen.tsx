import { Stack } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { ObjectivesList } from './ObjectivesList';

/** /objectives: the Backlog list (the dashboard renders its Objectives page, titled Backlog, here). */
export function ObjectivesScreen() {
  const { t } = useLocale();
  const { colors } = useTheme();
  return (
    <View style={[styles.fill, { backgroundColor: colors.background }]}>
      <Stack.Screen options={{ title: t('Backlog') }} />
      <ObjectivesList />
    </View>
  );
}

const styles = StyleSheet.create({ fill: { flex: 1 } });
