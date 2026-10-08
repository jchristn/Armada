import { useState } from 'react';
import { ActivityIndicator, StyleSheet, View } from 'react-native';
import { getVesselBranches, mergeVesselBranch, pushVesselBranch, type BranchInfo } from '@dashboard/api/client';
import { SwitchField } from '../../build/fields';
import { errorMessage, useLiveResource } from '../../build/useLiveResource';
import { AppText, Button, StatusBadge } from '../../components/ui';
import { SelectField, type SelectOption } from '../../components/ui/SelectSheet';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing, typography } from '../../theme/typography';

/**
 * Branch management for a vessel (the dashboard's BranchesModal): every branch with its default / current flags,
 * ahead / behind counts and last commit, a Push button per branch, and "Merge a branch" (source into target, with
 * optional push).
 */
export function Branches({ vesselId }: { vesselId: string }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const { pushToast } = useNotifications();
  const resource = useLiveResource(() => getVesselBranches(vesselId), [vesselId]);
  const branches: BranchInfo[] = resource.data?.branches || [];
  const error = resource.error ?? resource.data?.error ?? '';
  const loading = resource.loading;
  const load = resource.reload;
  const [busy, setBusy] = useState('');
  const [source, setSource] = useState('');
  const [target, setTarget] = useState('');
  const [push, setPush] = useState(true);

  async function handlePush(branch: string) {
    setBusy(`push:${branch}`);
    try {
      await pushVesselBranch(vesselId, branch);
      pushToast('success', t('Pushed {{branch}}', { branch }));
      await load();
    } catch (e) {
      pushToast('error', t('Push failed: {{message}}', { message: errorMessage(e) }));
    } finally {
      setBusy('');
    }
  }

  async function handleMerge() {
    if (!source || !target) { pushToast('warning', t('Select a source and a target branch.')); return; }
    if (source === target) { pushToast('warning', t('Source and target must differ.')); return; }
    setBusy('merge');
    try {
      await mergeVesselBranch(vesselId, source, target, push);
      pushToast('success', t('Merged {{source}} into {{target}}', { source, target }));
      await load();
    } catch (e) {
      pushToast('error', t('Merge failed: {{message}}', { message: errorMessage(e) }));
    } finally {
      setBusy('');
    }
  }

  const options: SelectOption<string>[] = [{ value: '', label: t('Select...') }, ...branches.map((b) => ({ value: b.name, label: b.name }))];

  return (
    <View>
      {error ? <AppText color="danger" style={styles.error} accessibilityRole="alert">{error}</AppText> : null}
      {loading && branches.length === 0 ? (
        <View style={styles.loading}><ActivityIndicator color={colors.primary} /><AppText muted>{t('Loading branches...')}</AppText></View>
      ) : branches.length === 0 ? (
        <AppText muted style={styles.empty}>{t('No branches found.')}</AppText>
      ) : (
        branches.map((b) => (
          <View key={b.name} style={[styles.branch, { borderColor: colors.border }]} testID={`vessel-branch-${b.name}`}>
            <View style={styles.branchHead}>
              <AppText selectable style={[typography.mono, styles.flex]} numberOfLines={2}>{b.name}</AppText>
              {b.isDefault ? <StatusBadge label={t('default')} tone="info" /> : null}
              {b.isCurrent ? <StatusBadge label={t('current')} tone="success" /> : null}
            </View>
            <AppText variant="caption" accessibilityLabel={`${t('Ahead / Behind')}: +${b.ahead} / -${b.behind}`}>
              <AppText variant="caption" color="success">{`+${b.ahead}`}</AppText>
              {' / '}
              <AppText variant="caption" color="danger">{`-${b.behind}`}</AppText>
            </AppText>
            <AppText variant="caption" muted numberOfLines={2}>{b.commitSubject || '-'}</AppText>
            <AppText variant="caption" muted>
              {[b.commitHash, b.commitDate ? new Date(b.commitDate).toLocaleString() : ''].filter(Boolean).join(' -- ') || '-'}
            </AppText>
            <Button
              label={busy === `push:${b.name}` ? t('Pushing...') : t('Push')}
              variant="secondary"
              icon="cloud-upload-outline"
              onPress={() => void handlePush(b.name)}
              busy={busy === `push:${b.name}`}
              disabled={busy !== '' && busy !== `push:${b.name}`}
              testID={`vessel-branch-push-${b.name}`}
              style={styles.push}
            />
          </View>
        ))
      )}

      <AppText variant="subheading" muted accessibilityRole="header" style={styles.heading}>{t('Merge a branch')}</AppText>
      <SelectField label={t('Source')} value={source} options={options} onChange={setSource} closeLabel={t('Close')} testID="vessel-merge-source" />
      <SelectField label={t('Target')} value={target} options={options} onChange={setTarget} closeLabel={t('Close')} testID="vessel-merge-target" />
      <SwitchField label={t('Push after merge')} value={push} onChange={setPush} testID="vessel-merge-push" />
      <Button label={busy === 'merge' ? t('Merging...') : t('Merge')} onPress={() => void handleMerge()} busy={busy === 'merge'} disabled={busy !== '' && busy !== 'merge'} testID="vessel-merge" />
      <Button label={t('Refresh')} variant="ghost" icon="refresh" onPress={() => void load()} disabled={loading} />
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  error: { marginBottom: spacing.md },
  loading: { alignItems: 'center', gap: spacing.sm, padding: spacing.lg },
  empty: { padding: spacing.lg, textAlign: 'center' },
  branch: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, padding: spacing.md, marginBottom: spacing.sm, gap: 2 },
  branchHead: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  push: { marginTop: spacing.sm, marginBottom: 0, alignSelf: 'flex-start' },
  heading: { textTransform: 'uppercase', marginTop: spacing.lg, marginBottom: spacing.md },
});
