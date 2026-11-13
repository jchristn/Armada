import { useRouter, type Href } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';
import { getVoyageTree } from '@dashboard/api/client';
import { baseTitle, branchLabel, countRunning, hasPipeline, stageLabel, treeIsInformative, treeRows } from '@dashboard/lib/missionTree';
import type { VoyageTree } from '@dashboard/types/models';
import { EntityStatusBadge } from '../../../components/app/EntityStatusBadge';
import { AppText, Section } from '../../../components/ui';
import { useLiveRefresh } from '../../../data/useLiveRefresh';
import { useQuery } from '../../../data/useQuery';
import { useLocale } from '../../../i18n/LocaleContext';
import { useTheme } from '../../../theme/ThemeContext';
import { MIN_TOUCH, spacing } from '../../../theme/typography';

const INDENT = spacing.lg;

/** Pure view of a loaded tree: each logical mission indented under its parent, with its pipeline and branch marker. */
export function VoyageTreeList({ tree, missionLimit }: { tree: VoyageTree; missionLimit?: number }) {
  const { t } = useLocale();
  const router = useRouter();
  const { colors } = useTheme();
  const running = countRunning(tree.roots);
  return (
    <View testID="voyage-tree" accessibilityRole="list">
      <AppText variant="caption" muted style={styles.pad} testID="voyage-tree-running">
        {missionLimit === undefined ? t('{{running}} running', { running })
          : missionLimit === 0 ? t('{{running}} running (no limit)', { running })
            : t('{{running}} / {{limit}} running', { running, limit: missionLimit })}
      </AppText>
      {treeRows(tree.roots).map(({ depth, node }) => {
        const branch = branchLabel(node.mission);
        const detail = [hasPipeline(node) ? [node.mission, ...node.stages].map(st => `${t(stageLabel(st))}: ${t(st.status)}`).join(' > ') : null, branch ? t(branch) : null]
          .filter(Boolean).join('  ·  ');
        return (
          <Pressable
            key={node.mission.id}
            accessibilityRole="link"
            accessibilityLabel={[baseTitle(node.mission.title), t(node.mission.status), detail || null].filter(Boolean).join(', ')}
            accessibilityHint={t('Opens the mission')}
            onPress={() => router.push(`/missions/${node.mission.id}` as Href)}
            style={({ pressed }) => [styles.row, { paddingLeft: spacing.lg + depth * INDENT, borderBottomColor: colors.border, opacity: pressed ? 0.6 : 1 }]}
            testID={`voyage-tree-node-${node.mission.id}`}
          >
            <View style={styles.head}>
              {depth > 0 ? <View style={[styles.guide, { backgroundColor: colors.border }]} /> : null}
              <AppText variant="label" style={styles.flex} numberOfLines={2}>{baseTitle(node.mission.title)}</AppText>
              <EntityStatusBadge status={node.mission.status} />
            </View>
            {detail ? <AppText variant="caption" muted>{detail}</AppText> : null}
          </Pressable>
        );
      })}
      {tree.truncated ? <AppText variant="caption" muted style={styles.pad}>{t('Showing the oldest 1000 of {{count}} missions; the table below lists them all.', { count: tree.totalMissions })}</AppText> : null}
    </View>
  );
}

/**
 * The voyage's missions as a tree (GET /api/v1/voyages/{id}/tree). Hidden for a flat voyage without pipeline stages,
 * where the mission list below says the same, and when the tree cannot be read.
 */
export function VoyageTreeSection({ voyageId, missionLimit }: { voyageId: string; missionLimit?: number }) {
  const { t } = useLocale();
  const query = useQuery<VoyageTree>(() => getVoyageTree(voyageId), [voyageId], t('Failed to load the mission tree.'));
  const reload = query.reload;
  useLiveRefresh(['mission.', 'voyage.'], () => { void reload(); });
  if (!treeIsInformative(query.data)) return null;
  return (
    <Section title={t('Mission Tree')} footer={t('A child starts after its parent\'s last stage finishes and builds on its result; missions at the same level run in parallel.')}>
      <VoyageTreeList tree={query.data!} missionLimit={missionLimit} />
    </Section>
  );
}

const styles = StyleSheet.create({
  row: { paddingRight: spacing.lg, paddingVertical: spacing.sm, borderBottomWidth: StyleSheet.hairlineWidth, gap: spacing.xs, minHeight: MIN_TOUCH, justifyContent: 'center' },
  head: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  guide: { width: 2, alignSelf: 'stretch', borderRadius: 1 },
  flex: { flex: 1 },
  pad: { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm },
});
