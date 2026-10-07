import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import type { CliPermissionRequest, CliPermissionRuleScope } from '@dashboard/types/models';
import { decideApproval } from '../../approvals/actions';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';
import { AppText } from '../ui/AppText';
import { BottomSheet } from '../ui/BottomSheet';
import { Button } from '../ui/Button';
import { SelectField, type SelectOption } from '../ui/SelectSheet';
import { TextField } from '../ui/TextField';

interface CliPermissionDecisionControlsProps {
  request: CliPermissionRequest;
  /** Called with the updated request (the server's copy when it returns one). */
  onDecided?: (updated: CliPermissionRequest) => void;
  /** What the buttons act on (spoken as their hint). */
  describedBy?: string;
}

/**
 * Allow once / Allow and remember / Deny for a pending CLI permission request (the dashboard's
 * CliPermissionDecisionControls). Allow and remember (only when the caller may create rules) opens a sheet to edit
 * the allow-rule pattern, prefilled with the suggested rule, and pick its scope; Deny takes an optional message the
 * captain sees. Decisions go through approvals/actions (decideApproval), the same path as push actions.
 */
export function CliPermissionDecisionControls({ request, onDecided, describedBy }: CliPermissionDecisionControlsProps) {
  const { t } = useLocale();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [rememberOpen, setRememberOpen] = useState(false);
  const [denyOpen, setDenyOpen] = useState(false);
  const [pattern, setPattern] = useState('');
  const [scope, setScope] = useState<CliPermissionRuleScope>('Captain');
  const [message, setMessage] = useState('');

  async function decide(approve: boolean, options: { comment?: string; remember?: { pattern: string; scope: CliPermissionRuleScope } } = {}): Promise<boolean> {
    setBusy(true);
    setError('');
    try {
      const result = await decideApproval({ kind: 'cli_permission', requestId: request.id }, approve ? 'approve' : 'deny', options);
      if (result.cliPermission) onDecided?.(result.cliPermission);
      return true;
    } catch (err: unknown) {
      setError(err instanceof Error && err.message ? err.message : t('Permission decision failed.'));
      return false;
    } finally {
      setBusy(false);
    }
  }

  function openRemember() {
    setPattern(request.suggestedRule || request.toolName || '');
    setScope(request.captainId ? 'Captain' : request.vesselId ? 'Vessel' : 'Global');
    setError('');
    setRememberOpen(true);
  }

  async function confirmRemember() {
    const trimmed = pattern.trim();
    if (!trimmed) return;
    if (await decide(true, { remember: { pattern: trimmed, scope } })) setRememberOpen(false);
  }

  async function confirmDeny() {
    const trimmed = message.trim();
    if (await decide(false, trimmed ? { comment: trimmed } : {})) setDenyOpen(false);
  }

  const scopes: SelectOption<CliPermissionRuleScope>[] = [];
  if (request.captainId) scopes.push({ value: 'Captain', label: request.captainName ? t('Captain {{name}}', { name: request.captainName }) : t('This captain') });
  if (request.vesselId) scopes.push({ value: 'Vessel', label: request.vesselName ? t('Vessel {{name}}', { name: request.vesselName }) : t('This vessel') });
  scopes.push({ value: 'Global', label: t('Everywhere (global)') });

  return (
    <View style={styles.wrap}>
      <View style={styles.row}>
        <Button
          label={busy && !rememberOpen && !denyOpen ? t('Working...') : t('Allow once')}
          onPress={() => void decide(true)}
          disabled={busy}
          accessibilityHint={describedBy}
          testID={`cli-allow-${request.id}`}
          style={styles.button}
        />
        {request.canRemember ? (
          <Button label={t('Allow and remember')} variant="secondary" onPress={openRemember} disabled={busy} accessibilityHint={describedBy} testID={`cli-remember-${request.id}`} style={styles.button} />
        ) : null}
        <Button label={t('Deny')} variant="danger" onPress={() => { setMessage(''); setError(''); setDenyOpen(true); }} disabled={busy} accessibilityHint={describedBy} testID={`cli-deny-${request.id}`} style={styles.button} />
      </View>
      {error && !rememberOpen && !denyOpen ? <AppText variant="caption" color="danger" accessibilityRole="alert">{error}</AppText> : null}

      <BottomSheet open={rememberOpen} title={t('Allow and remember')} onClose={() => { if (!busy) setRememberOpen(false); }} closeLabel={t('Cancel')} testID="cli-remember-sheet">
        <AppText muted style={styles.subtitle}>{t('Allows this call now and creates an allow rule so matching calls run without asking.')}</AppText>
        {error ? <AppText variant="caption" color="danger" accessibilityRole="alert">{error}</AppText> : null}
        <TextField
          label={t('Rule pattern')}
          value={pattern}
          onChangeText={setPattern}
          autoCapitalize="none"
          autoCorrect={false}
          hint={t('Claude Code permission rule syntax, for example Bash(git status:*) or WebFetch(domain:example.com).')}
          testID="cli-remember-pattern"
        />
        <SelectField label={t('Remember for')} value={scope} options={scopes} onChange={setScope} closeLabel={t('Close')} testID="cli-remember-scope" />
        <Button label={busy ? t('Working...') : t('Allow and create rule')} onPress={() => void confirmRemember()} disabled={busy || !pattern.trim()} busy={busy} testID="cli-remember-confirm" />
        <Button label={t('Cancel')} variant="ghost" onPress={() => setRememberOpen(false)} disabled={busy} />
      </BottomSheet>

      <BottomSheet open={denyOpen} title={t('Deny')} onClose={() => { if (!busy) setDenyOpen(false); }} closeLabel={t('Cancel')} testID="cli-deny-sheet">
        <AppText muted style={styles.subtitle}>{t('The captain is told the tool call was denied.')}</AppText>
        {error ? <AppText variant="caption" color="danger" accessibilityRole="alert">{error}</AppText> : null}
        <TextField
          label={t('Optional message for the captain')}
          value={message}
          onChangeText={setMessage}
          multiline
          placeholder={t('e.g., Use the test script instead of deleting files.')}
          testID="cli-deny-message"
        />
        <Button label={busy ? t('Working...') : t('Deny')} variant="danger" onPress={() => void confirmDeny()} disabled={busy} busy={busy} testID="cli-deny-confirm" />
        <Button label={t('Cancel')} variant="ghost" onPress={() => setDenyOpen(false)} disabled={busy} />
      </BottomSheet>
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { gap: spacing.xs },
  row: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  button: { marginBottom: 0 },
  subtitle: { marginBottom: spacing.md },
});
