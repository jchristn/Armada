import { useRouter } from 'expo-router';
import type { ComponentType } from 'react';
import { StyleSheet, View } from 'react-native';
import { useSplitSelection } from '../components/app/useSplitSelection';
import { AppText, Button, SplitView } from '../components/ui';
import { useLocale } from '../i18n/LocaleContext';
import { spacing } from '../theme/typography';
import { DockDetail } from './operations/DockDetail';
import { DocksList } from './operations/DocksList';
import { EventDetail } from './operations/EventDetail';
import { EventsList } from './operations/EventsList';
import type { OperationsDetailProps, OperationsListProps } from './operations/listTypes';
import { SignalDetail } from './operations/SignalDetail';
import { SignalsList } from './operations/SignalsList';

/** A list built ahead of the hub that hosts it on the dashboard (for example Docks before the Captains hub). */
export interface EmbeddedSection {
  label: string;
  List: ComponentType<OperationsListProps>;
  Detail: ComponentType<OperationsDetailProps>;
  /** The item's own route (phones push it). */
  route: (id: string) => string;
}

export interface EmbeddedHub {
  /** The query parameter the dashboard hub selects its tab with (?tab= or ?source=). */
  param: string;
  sections: Record<string, EmbeddedSection>;
}

/**
 * Placeholder hubs that already serve some of their sections. The dashboard shows the Docks list as the Captains
 * hub tab (/captains?tab=docks, W3.2) and Signals and Events as Activity sources (/activity?source=..., W4.3); W2.4
 * built those lists first, so the hub placeholders serve them until W3.2 and W4.3 replace the hub screens (which
 * then embed the same list components).
 */
export const EMBEDDED_SECTIONS: Record<string, EmbeddedHub> = {
  '/captains': {
    param: 'tab',
    sections: {
      docks: { label: 'Docks', List: DocksList, Detail: DockDetail, route: (id) => `/docks/${id}` },
    },
  },
  '/activity': {
    param: 'source',
    sections: {
      events: { label: 'Events', List: EventsList, Detail: EventDetail, route: (id) => `/events/${id}` },
      signals: { label: 'Signals', List: SignalsList, Detail: SignalDetail, route: (id) => `/signals/${id}` },
    },
  },
};

/** Pure: the embedded section a placeholder route and its query select, if any. */
export function embeddedSectionFor(pattern: string, params: Record<string, string | string[] | undefined>): { key: string; section: EmbeddedSection } | null {
  const hub = EMBEDDED_SECTIONS[pattern];
  if (!hub) return null;
  const raw = params[hub.param];
  const key = (Array.isArray(raw) ? raw[0] : raw) ?? '';
  const section = hub.sections[key];
  return section ? { key, section } : null;
}

/**
 * One embedded section, full screen: its list with the detail beside it on tablets (or pushed on phones), under a
 * note that the rest of the hub arrives in its workstream and a way back to the hub's overview.
 */
export function EmbeddedSectionView({ hubTitle, workstream, param, sectionKey, section }: {
  hubTitle: string;
  workstream: string;
  param: string;
  sectionKey: string;
  section: EmbeddedSection;
}) {
  const { t } = useLocale();
  const router = useRouter();
  const selection = useSplitSelection(section.route);
  const { List, Detail } = section;
  const list = (
    <View style={styles.fill}>
      <View style={styles.note}>
        <AppText variant="caption" muted style={styles.flex} testID="embedded-section-note">
          {t('{{section}} in {{hub}}; the rest of {{hub}} is coming in {{workstream}}', { section: t(section.label), hub: hubTitle, workstream })}
        </AppText>
        <Button label={t('All sections')} variant="ghost" onPress={() => router.setParams({ [param]: '' })} testID="embedded-section-back" />
      </View>
      <List onSelect={selection.select} selectedId={selection.selectedId} />
    </View>
  );
  return (
    <View style={styles.fill} testID={`embedded-section-${sectionKey}`}>
      <SplitView
        master={list}
        detail={selection.selectedId ? <Detail key={selection.selectedId} id={selection.selectedId} embedded /> : (selection.isTablet ? <EmptyDetail /> : null)}
      />
    </View>
  );
}

function EmptyDetail() {
  const { t } = useLocale();
  return (
    <View style={styles.empty}>
      <AppText muted>{t('Select an item to see its details.')}</AppText>
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  flex: { flex: 1 },
  note: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, paddingHorizontal: spacing.lg, paddingTop: spacing.sm },
  empty: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: spacing.xl },
});
