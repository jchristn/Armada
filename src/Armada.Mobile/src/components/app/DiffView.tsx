import { useMemo, useState } from 'react';
import { FlatList, ScrollView, StyleSheet, View } from 'react-native';
import { parseUnifiedDiff, type UnifiedDiffLine } from '@dashboard/lib/unifiedDiff';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { spacing, typography } from '../../theme/typography';
import { AppText, EmptyState, SelectField } from '../ui';

/** Pure: whether a raw diff has nothing to show (the server answers 'No changes' / 'No modified files'). */
export function isEmptyDiff(raw: string | null | undefined): boolean {
  return !raw || !raw.trim() || raw === 'No changes' || raw === 'No modified files';
}

/**
 * A unified diff (the dashboard's DiffViewer): per-file picker with +/- counts, then the classified lines with old
 * and new line numbers. Lines are virtualized; long lines scroll sideways.
 */
export function DiffView({ rawDiff, testID }: { rawDiff: string; testID?: string }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const [file, setFile] = useState('');
  const parsed = useMemo(() => parseUnifiedDiff(isEmptyDiff(rawDiff) ? null : rawDiff), [rawDiff]);
  const lines = useMemo(() => {
    if (!file) return parsed.lines;
    const section = parsed.files[Number(file)];
    return section ? parsed.lines.slice(section.startLine, section.endLine) : parsed.lines;
  }, [parsed, file]);

  if (isEmptyDiff(rawDiff)) return <EmptyState icon="git-compare-outline" title={t('No changes')} />;

  const additions = parsed.files.reduce((sum, f) => sum + f.additions, 0);
  const deletions = parsed.files.reduce((sum, f) => sum + f.deletions, 0);

  const background = (line: UnifiedDiffLine): string | undefined => {
    if (line.kind === 'add') return colors.success + '22';
    if (line.kind === 'del') return colors.danger + '22';
    if (line.kind === 'hunkHeader' || line.kind === 'fileHeader') return colors.surfaceRaised;
    return undefined;
  };
  const tint = (line: UnifiedDiffLine) => (line.kind === 'add' ? 'success' : line.kind === 'del' ? 'danger' : line.kind === 'hunkHeader' ? 'info' : line.kind === 'fileHeader' || line.kind === 'meta' ? 'textMuted' : 'text') as 'success' | 'danger' | 'info' | 'textMuted' | 'text';

  return (
    <View style={styles.fill} testID={testID}>
      <View style={styles.head}>
        <AppText variant="caption" muted testID={testID ? `${testID}-summary` : undefined}>
          {t('{{files}} file(s), +{{additions}} -{{deletions}}', { files: parsed.files.length, additions, deletions })}
        </AppText>
        {parsed.files.length > 1 ? (
          <SelectField
            label={t('File')}
            value={file}
            onChange={setFile}
            allowEmpty
            placeholder={t('All files')}
            closeLabel={t('Close')}
            searchLabel={t('Search files')}
            options={parsed.files.map((f, i) => ({ value: String(i), label: f.path, description: `${t(f.kind)} +${f.additions} -${f.deletions}` }))}
            testID={testID ? `${testID}-file` : undefined}
          />
        ) : null}
      </View>
      <ScrollView horizontal style={styles.fill} contentContainerStyle={styles.minWidth}>
        <FlatList
          data={lines}
          keyExtractor={(_line, index) => String(index)}
          initialNumToRender={60}
          windowSize={11}
          renderItem={({ item }) => (
            <View style={[styles.line, { backgroundColor: background(item) }]}>
              <AppText style={[typography.mono, styles.num]} muted>{item.oldNumber ?? ''}</AppText>
              <AppText style={[typography.mono, styles.num]} muted>{item.newNumber ?? ''}</AppText>
              <AppText selectable style={typography.mono} color={tint(item)}>{item.text || ' '}</AppText>
            </View>
          )}
        />
      </ScrollView>
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  minWidth: { minWidth: '100%' },
  head: { paddingHorizontal: spacing.md, paddingTop: spacing.sm, gap: spacing.sm },
  line: { flexDirection: 'row', paddingHorizontal: spacing.sm },
  num: { width: 44, textAlign: 'right', paddingRight: spacing.sm, fontSize: 12 },
});
