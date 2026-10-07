import { ScrollView, StyleSheet } from 'react-native';
import { AppText } from '../../components/ui';
import { typography } from '../../theme/typography';

/** The dashboard's JsonViewer body: a record as formatted, selectable JSON (shown in a sheet). */
export function JsonView({ data }: { data: unknown }) {
  return (
    <ScrollView horizontal>
      <AppText selectable style={[typography.mono, styles.text]} testID="json-view">{JSON.stringify(data, null, 2)}</AppText>
    </ScrollView>
  );
}

const styles = StyleSheet.create({ text: { fontSize: 12, lineHeight: 17 } });
