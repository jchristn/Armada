import { useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, View } from 'react-native';
import { apiErrorCode, browseVesselImport } from '@dashboard/api/client';
import type { VesselBrowseEntry } from '@dashboard/types/models';
import { importErrorLabel } from '@dashboard/lib/vesselImportLabels';
import { errorMessage } from '../../../build/useLiveResource';
import { AppText, Button, Icon, StatusBadge } from '../../../components/ui';
import { useLocale } from '../../../i18n/LocaleContext';
import { useTheme } from '../../../theme/ThemeContext';
import { MIN_TOUCH, spacing } from '../../../theme/typography';

interface NodeState {
  loading: boolean;
  error: string;
  entries: VesselBrowseEntry[] | null;
  expanded: boolean;
}

export interface BrowseTreeProps {
  selected: string[];
  onToggle: (path: string) => void;
  /** When false, worktrees are shown but cannot be selected. */
  allowWorktrees: boolean;
}

/**
 * Lazily expanded folder tree over the Admiral host (limited by the server to the allowed import roots), the
 * dashboard's BrowseTree: git repositories and worktrees are marked, and checking a folder adds it to discovery.
 */
export function BrowseTree({ selected, onToggle, allowWorktrees }: BrowseTreeProps) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const [roots, setRoots] = useState<VesselBrowseEntry[] | null>(null);
  const [rootError, setRootError] = useState('');
  const [rootLoading, setRootLoading] = useState(false);
  const [nodes, setNodes] = useState<Record<string, NodeState>>({});

  const describe = useCallback((e: unknown) => importErrorLabel(t, apiErrorCode(e), errorMessage(e) || t('Failed to list the directory.')), [t]);

  const loadRoots = useCallback(async () => {
    setRootLoading(true);
    setRootError('');
    try {
      const result = await browseVesselImport(null);
      setRoots(result?.entries ?? []);
    } catch (e) {
      setRootError(describe(e));
    } finally {
      setRootLoading(false);
    }
  }, [describe]);

  useEffect(() => { void loadRoots(); }, [loadRoots]);

  async function loadNode(entry: VesselBrowseEntry) {
    setNodes((n) => ({ ...n, [entry.path]: { loading: true, error: '', entries: null, expanded: true } }));
    try {
      const result = await browseVesselImport(entry.path);
      setNodes((n) => ({ ...n, [entry.path]: { loading: false, error: '', entries: result?.entries ?? [], expanded: true } }));
    } catch (e) {
      setNodes((n) => ({ ...n, [entry.path]: { loading: false, error: describe(e), entries: null, expanded: true } }));
    }
  }

  function toggleExpand(entry: VesselBrowseEntry) {
    const current = nodes[entry.path];
    if (current?.expanded) setNodes((n) => ({ ...n, [entry.path]: { ...current, expanded: false } }));
    else if (current?.entries) setNodes((n) => ({ ...n, [entry.path]: { ...current, expanded: true } }));
    else void loadNode(entry);
  }

  function renderEntries(entries: VesselBrowseEntry[], depth: number) {
    if (entries.length === 0) {
      return <AppText variant="caption" muted style={{ paddingLeft: depth * 16 + 40 }}>{t('No subdirectories')}</AppText>;
    }
    return entries.map((entry) => {
      const node = nodes[entry.path];
      const disabled = entry.isWorktree && !allowWorktrees;
      const checked = selected.includes(entry.path);
      const expandable = entry.hasSubdirectories && !entry.isGitRepository;
      return (
        <View key={entry.path}>
          <View style={[styles.row, { paddingLeft: depth * 16, borderBottomColor: colors.border }]}>
            {expandable ? (
              <Pressable
                accessibilityRole="button"
                accessibilityLabel={node?.expanded ? t('Collapse {{name}}', { name: entry.name }) : t('Expand {{name}}', { name: entry.name })}
                accessibilityState={{ expanded: !!node?.expanded }}
                onPress={() => toggleExpand(entry)}
                style={styles.expand}
                testID={`browse-expand-${entry.name}`}
              >
                <Icon name={node?.expanded ? 'chevron-down' : 'chevron-forward'} size={18} color="textMuted" />
              </Pressable>
            ) : <View style={styles.expand} />}
            <Pressable
              accessibilityRole="checkbox"
              accessibilityLabel={entry.path}
              accessibilityState={{ checked, disabled: disabled && !checked }}
              disabled={disabled && !checked}
              onPress={() => onToggle(entry.path)}
              style={[styles.check, { opacity: disabled && !checked ? 0.5 : 1 }]}
              testID={`browse-select-${entry.name}`}
            >
              <Icon name={checked ? 'checkbox' : 'square-outline'} color={checked ? 'primary' : 'textMuted'} />
              <AppText style={styles.flex} numberOfLines={1}>{entry.name}</AppText>
            </Pressable>
            {entry.isGitRepository ? <StatusBadge label={t('Git repository')} tone="success" /> : null}
            {entry.isWorktree ? <StatusBadge label={t('Worktree')} tone="warning" /> : null}
          </View>
          {node?.expanded ? (
            <View>
              {node.loading ? <ActivityIndicator style={{ marginLeft: (depth + 1) * 16 + 40 }} color={colors.primary} accessibilityLabel={t('Loading...')} /> : null}
              {node.error ? (
                <View style={[styles.error, { paddingLeft: (depth + 1) * 16 + 40 }]}>
                  <AppText variant="caption" color="danger">{node.error}</AppText>
                  <Button label={t('Retry')} variant="ghost" onPress={() => void loadNode(entry)} />
                </View>
              ) : null}
              {node.entries ? renderEntries(node.entries, depth + 1) : null}
            </View>
          ) : null}
        </View>
      );
    });
  }

  return (
    <View accessibilityLabel={t('Folders on the Admiral host')} testID="browse-tree">
      {rootLoading ? <AppText muted>{t('Loading allowed roots...')}</AppText> : null}
      {rootError ? (
        <View style={styles.error}>
          <AppText color="danger">{rootError}</AppText>
          <Button label={t('Retry')} variant="ghost" onPress={() => void loadRoots()} />
        </View>
      ) : null}
      {roots && roots.length === 0 ? <AppText muted>{t('No browsable folders. An administrator can configure allowed roots under Settings > Import.')}</AppText> : null}
      {roots && roots.length > 0 ? renderEntries(roots, 0) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs, minHeight: MIN_TOUCH, borderBottomWidth: StyleSheet.hairlineWidth },
  expand: { width: 36, minHeight: MIN_TOUCH, alignItems: 'center', justifyContent: 'center' },
  check: { flex: 1, flexDirection: 'row', alignItems: 'center', gap: spacing.sm, minHeight: MIN_TOUCH },
  error: { gap: spacing.xs },
});
