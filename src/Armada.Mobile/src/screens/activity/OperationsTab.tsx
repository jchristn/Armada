import type { ComponentType } from 'react';
import { MasterDetail, useSelection } from '../../components/resource/Hub';
import type { OperationsDetailProps, OperationsListProps } from '../operations/listTypes';

/**
 * An Activity source served by W2's Operations list and detail (Events, Signals): phones push the item's own route
 * (/events/:id, /signals/:id); tablets show the detail beside the list.
 */
export function OperationsTab({ List, Detail, route }: {
  List: ComponentType<OperationsListProps>;
  Detail: ComponentType<OperationsDetailProps>;
  route: (id: string) => string;
}) {
  const selection = useSelection(route);
  return (
    <MasterDetail
      list={<List onSelect={selection.open} selectedId={selection.selected} />}
      detail={selection.selected ? <Detail key={selection.selected} id={selection.selected} embedded /> : null}
    />
  );
}
