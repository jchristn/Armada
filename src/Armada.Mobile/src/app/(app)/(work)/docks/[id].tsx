import { useLocalSearchParams } from 'expo-router';
import { DockDetail } from '@/screens/operations/DockDetail';

/** /docks/:id: one dock (W2.4). */
export default function Screen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <DockDetail id={String(id ?? '')} />;
}
