import { useLocalSearchParams } from 'expo-router';
import { ListDetailRoute } from '@/navigation/listDetail';
import { ConfigurationHub } from '@/screens/configuration/ConfigurationHub';
import { SkillDetailRoute } from '@/screens/configuration/SkillDetail';

/**
 * /skills/:id: one item. Opened on a window wide enough for list and detail, it shows the Configuration hub's Skills
 * list with this item selected beside it (deep links and notifications land with the list on the left); narrower
 * windows show the item alone.
 */
export default function Screen() {
  const params = useLocalSearchParams<{ id?: string }>();
  const key = String(params.id ?? '');
  return (
    <ListDetailRoute
      path={`/skills/${key}`}
      id={key === 'new' ? '' : key}
      tab="skills"
      list={<ConfigurationHub />}
      detail={<SkillDetailRoute />}
    />
  );
}
