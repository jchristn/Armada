import { useLocalSearchParams } from 'expo-router';
import { ListDetailRoute } from '@/navigation/listDetail';
import { ConfigurationHub } from '@/screens/configuration/ConfigurationHub';
import { ProjectProfileDetailRoute } from '@/screens/configuration/ProjectProfileDetail';

/**
 * /project-profiles/:id: one item. Opened on a window wide enough for list and detail, it shows the Configuration
 * hub's Project Profiles list with this item selected beside it (deep links and notifications land with the list on
 * the left); narrower windows show the item alone.
 */
export default function Screen() {
  const params = useLocalSearchParams<{ id?: string }>();
  const key = String(params.id ?? '');
  return (
    <ListDetailRoute
      path={`/project-profiles/${key}`}
      id={key === 'new' ? '' : key}
      tab="project-profiles"
      list={<ConfigurationHub />}
      detail={<ProjectProfileDetailRoute />}
    />
  );
}
