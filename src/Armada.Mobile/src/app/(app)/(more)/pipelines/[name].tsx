import { useLocalSearchParams } from 'expo-router';
import { ListDetailRoute } from '@/navigation/listDetail';
import { ConfigurationHub } from '@/screens/configuration/ConfigurationHub';
import { PipelineDetailRoute } from '@/screens/configuration/PipelineDetail';
import { pipelinePath } from '@/screens/configuration/common';

/**
 * /pipelines/:name: one item. Opened on a window wide enough for list and detail, it shows the Configuration hub's
 * Pipelines list with this item selected beside it (deep links and notifications land with the list on the left);
 * narrower windows show the item alone.
 */
export default function Screen() {
  const params = useLocalSearchParams<{ name?: string }>();
  const key = String(params.name ?? '');
  return (
    <ListDetailRoute
      path={pipelinePath(key)}
      id={key === 'new' ? '' : key}
      tab="pipelines"
      list={<ConfigurationHub />}
      detail={<PipelineDetailRoute />}
    />
  );
}
