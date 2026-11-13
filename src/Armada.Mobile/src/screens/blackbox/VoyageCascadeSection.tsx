import { useRouter, type Href } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';
import { getVoyageBlackBoxCascade } from '@dashboard/api/client';
import type { BlackBoxCascadeGraph, BlackBoxCascadeNode } from '@dashboard/types/blackbox';
import { EntityStatusBadge } from '../../components/app/EntityStatusBadge';
import { AppText, Button, Section, StatusBadge } from '../../components/ui';
import { useLiveRefresh } from '../../data/useLiveRefresh';
import { useQuery } from '../../data/useQuery';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, spacing } from '../../theme/typography';
import { spaced } from './blackBoxModel';
import { cascadeHasFailures, cascadeNodeName, cascadeRelations, orderedCascadeNodes } from './insightModel';

/** Pure view of a loaded graph: each mission (root first) with its status, failure kind and the edges touching it. */
export function VoyageCascadeList({ graph }: { graph: BlackBoxCascadeGraph }) {
  const { t } = useLocale();
  const router = useRouter();
  const { colors } = useTheme();
  const open = (path: string) => router.push(path as Href);
  return (
    <View testID="voyage-cascade" accessibilityRole="list">
      {orderedCascadeNodes(graph).map((node: BlackBoxCascadeNode) => {
        const relations = cascadeRelations(graph, node.missionId);
        return (
          <View key={node.missionId} style={[styles.node, { borderBottomColor: colors.border }]} testID={`cascade-node-${node.missionId}`}>
            <Pressable
              accessibilityRole="link"
              accessibilityLabel={[node.title || node.missionId, t(node.status), node.isRoot ? t('Root cause') : null].filter(Boolean).join(', ')}
              accessibilityHint={t('Opens the mission')}
              onPress={() => open(`/missions/${node.missionId}`)}
              style={({ pressed }) => [styles.head, { opacity: pressed ? 0.6 : 1 }]}
              testID={`cascade-open-${node.missionId}`}
            >
              <AppText variant="label" style={styles.flex}>{node.title || node.missionId}</AppText>
              <EntityStatusBadge status={node.status} />
            </Pressable>
            <View style={styles.badges}>
              {node.isRoot ? <StatusBadge label={t('Root cause')} tone="failed" /> : null}
              {node.failureKind ? <StatusBadge label={t(spaced(node.failureKind))} tone="warning" /> : null}
            </View>
            {relations.map((relation, index) => {
              const label = t(relation.label, { title: cascadeNodeName(graph, relation.otherMissionId) });
              return (
                <Pressable
                  key={`${relation.edge.kind}-${relation.edge.fromMissionId}-${relation.edge.toMissionId}-${index}`}
                  accessibilityRole="link"
                  accessibilityLabel={label}
                  accessibilityHint={t('Opens the mission')}
                  onPress={() => open(`/missions/${relation.otherMissionId}`)}
                  style={({ pressed }) => [styles.edge, { opacity: pressed ? 0.6 : 1 }]}
                  testID={`cascade-edge-${node.missionId}-${index}`}
                >
                  <AppText variant="caption" color="primary">{label}</AppText>
                </Pressable>
              );
            })}
            {node.entryId ? (
              <Button label={t('Open black box entry')} variant="ghost" icon="open-outline" onPress={() => open(`/blackbox/${node.entryId}`)} testID={`cascade-entry-${node.missionId}`} />
            ) : null}
          </View>
        );
      })}
      {graph.truncated ? <AppText variant="caption" muted style={styles.pad} testID="voyage-cascade-truncated">{t('Only the first {{count}} missions are shown.', { count: graph.nodes.length })}</AppText> : null}
    </View>
  );
}

/**
 * The voyage's cascade graph as a list (GET /api/v1/voyages/{id}/blackbox/cascade): which failure took the others
 * down. Hidden while it loads, when the voyage has no failure to explain, and when the graph cannot be read (the
 * black box panel above shows the failure either way).
 */
export function VoyageCascadeSection({ voyageId }: { voyageId: string }) {
  const { t } = useLocale();
  const query = useQuery<BlackBoxCascadeGraph>(() => getVoyageBlackBoxCascade(voyageId), [voyageId], t('Failed to load the cascade.'));
  const reload = query.reload;
  useLiveRefresh(['blackbox.', 'mission.'], () => { void reload(); });
  const graph = query.data;
  if (!graph || !cascadeHasFailures(graph)) return null;
  return (
    <Section title={t('Cascade')} footer={t('Which failure took the other missions down. The root cause is listed first.')}>
      <VoyageCascadeList graph={graph} />
    </Section>
  );
}

const styles = StyleSheet.create({
  node: { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, borderBottomWidth: StyleSheet.hairlineWidth, gap: spacing.xs },
  head: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, minHeight: MIN_TOUCH },
  flex: { flex: 1 },
  badges: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs },
  edge: { minHeight: MIN_TOUCH, justifyContent: 'center' },
  pad: { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm },
});
