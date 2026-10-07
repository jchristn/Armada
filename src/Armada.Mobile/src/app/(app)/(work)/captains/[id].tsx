import { useLocalSearchParams } from 'expo-router';
import { CaptainDetailScreen } from '../../../../screens/captains/CaptainDetailScreen';

/** /captains/:id: one captain (state, policy, chat, tools, log, recent missions). */
export default function CaptainRoute() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <CaptainDetailScreen id={String(id ?? '')} />;
}
