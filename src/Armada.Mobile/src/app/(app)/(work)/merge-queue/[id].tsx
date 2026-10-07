import { useLocalSearchParams } from 'expo-router';
import { MergeEntryDetail } from '@/screens/operations/MergeEntryDetail';

/** /merge-queue/:id: one merge queue entry (W2.4). */
export default function Screen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <MergeEntryDetail id={String(id ?? '')} />;
}
