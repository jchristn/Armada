import { useLocalSearchParams } from 'expo-router';
import { BlackBoxEntryScreen } from '@/screens/blackbox/BlackBoxEntryScreen';

/** /blackbox/:id: one black box entry. */
export default function Screen() {
  const params = useLocalSearchParams<{ id?: string }>();
  const key = String(params.id ?? '');
  return <BlackBoxEntryScreen key={key} id={key} />;
}
