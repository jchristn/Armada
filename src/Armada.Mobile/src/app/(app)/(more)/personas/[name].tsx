import { useLocalSearchParams } from 'expo-router';
import { ListDetailRoute } from '@/navigation/listDetail';
import { ConfigurationHub } from '@/screens/configuration/ConfigurationHub';
import { PersonaDetailRoute } from '@/screens/configuration/PersonaDetail';
import { personaPath } from '@/screens/configuration/common';

/**
 * /personas/:name: one item. Opened on a window wide enough for list and detail, it shows the Configuration hub's
 * Personas list with this item selected beside it (deep links and notifications land with the list on the left);
 * narrower windows show the item alone.
 */
export default function Screen() {
  const params = useLocalSearchParams<{ name?: string }>();
  const key = String(params.name ?? '');
  return (
    <ListDetailRoute
      path={personaPath(key)}
      id={key === 'new' ? '' : key}
      tab="personas"
      list={<ConfigurationHub />}
      detail={<PersonaDetailRoute />}
    />
  );
}
