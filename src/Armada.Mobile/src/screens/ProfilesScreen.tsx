import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { useAuth } from '../auth/AuthContext';
import { ProfileForm } from '../components/app/ProfileForm';
import { AppText, Button, ConfirmDialog, EmptyState, Icon, ListRow, Screen, Section, SplitView, SwipeRow } from '../components/ui';
import { useLocale } from '../i18n/LocaleContext';
import { useLayout } from '../navigation/useLayout';
import type { ServerProfile } from '../profiles/types';
import { spacing } from '../theme/typography';

/**
 * Server profiles: saved Admirals, one active. Switching signs in with that profile's stored token (or shows sign-in).
 * Swipe a row to edit or delete; deleting asks for the typed word "delete", like the dashboard's destructive actions.
 */
export function ProfilesScreen() {
  const { profiles, activeProfile, selectProfile, saveProfile, deleteProfile, forgetSavedPassword } = useAuth();
  const { t } = useLocale();
  // List and editor side by side (the pane's width, not the device).
  const { split: isTablet } = useLayout();
  const [editing, setEditing] = useState<ServerProfile | 'new' | null>(null);
  const [deleting, setDeleting] = useState<ServerProfile | null>(null);

  const master = (
    <Screen testID="profiles">
      <Section title={t('Servers')}>
        {profiles.map((p) => (
          <SwipeRow
            key={p.id}
            testID={`profile-${p.id}`}
            actions={[
              { key: 'edit', label: t('Edit'), icon: 'create-outline', onPress: () => setEditing(p) },
              { key: 'delete', label: t('Delete'), icon: 'trash-outline', tone: 'danger', onPress: () => setDeleting(p) },
            ]}
          >
            <ListRow
              title={p.name}
              subtitle={`${p.url}${p.lastUserEmail ? `\n${p.lastUserEmail}` : ''}`}
              icon="server-outline"
              selected={p.id === activeProfile?.id}
              accessory={p.id === activeProfile?.id ? <Icon name="checkmark-circle" color="primary" /> : null}
              accessibilityValue={p.id === activeProfile?.id ? t('Active') : undefined}
              onPress={() => void selectProfile(p.id)}
              onLongPress={() => setEditing(p)}
              accessibilityHint={t('Switch to this server. Swipe or use actions to edit or delete.')}
            />
          </SwipeRow>
        ))}
        <ListRow testID="profiles-add" icon="add-circle-outline" title={t('Add server')} onPress={() => setEditing('new')} />
      </Section>
    </Screen>
  );

  const editor = editing ? (
    <Screen testID="profile-editor">
      <View style={styles.pad}>
        <AppText variant="heading" accessibilityRole="header" style={styles.title}>
          {editing === 'new' ? t('Add server') : t('Edit server')}
        </AppText>
        <ProfileForm
          key={editing === 'new' ? 'new' : editing.id}
          profile={editing === 'new' ? null : profiles.find((p) => p.id === editing.id) ?? editing}
          submitLabel={t('Save')}
          onForgetSavedPassword={editing === 'new' ? undefined : () => forgetSavedPassword(editing.id)}
          onCancel={() => setEditing(null)}
          onSubmit={async (draft) => {
            await saveProfile(draft, editing === 'new' ? undefined : editing.id);
            setEditing(null);
          }}
        />
        {editing !== 'new' ? <Button label={t('Delete')} variant="ghost" onPress={() => setDeleting(editing)} /> : null}
      </View>
    </Screen>
  ) : isTablet ? (
    <EmptyState icon="server-outline" title={t('Select a server to edit')} message={t('Long-press a server, or swipe it, to edit.')} />
  ) : null;

  return (
    <>
      <SplitView master={master} detail={editor} />
      <ConfirmDialog
        testID="profile-delete-confirm"
        open={!!deleting}
        title={t('Delete server?')}
        message={t('Removes {{name}} and its saved sign-in from this device. The server itself is not changed.', { name: deleting?.name ?? '' })}
        confirmLabel={t('Delete')}
        cancelLabel={t('Cancel')}
        danger
        typedConfirmation="delete"
        typedLabel={t('Type delete to confirm')}
        onConfirm={() => {
          const target = deleting;
          setDeleting(null);
          if (target) void deleteProfile(target.id);
          if (editing && editing !== 'new' && editing.id === target?.id) setEditing(null);
        }}
        onCancel={() => setDeleting(null)}
      />
    </>
  );
}

const styles = StyleSheet.create({
  pad: { paddingHorizontal: spacing.lg },
  title: { marginBottom: spacing.lg },
});
