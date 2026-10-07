import type { WorkflowProfileCommandPreview } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import DataTable from './DataTable';

interface WorkflowCommandPreviewProps {
  commands: WorkflowProfileCommandPreview[] | null | undefined;
  emptyMessage?: string;
}

export default function WorkflowCommandPreview({ commands, emptyMessage }: WorkflowCommandPreviewProps) {
  const { t } = useLocale();

  if (!commands || commands.length === 0) {
    return <p className="text-dim">{emptyMessage || t('No resolved commands available.')}</p>;
  }

  return (
    <DataTable
      tableKey="workflow-command-preview"
      wrapClassName="workflow-command-preview-table"
      recordCount={null}
      rows={commands.map((command, index) => ({ command, index }))}
      rowKey={({ command, index }) => `${command.checkType}-${command.environmentName || 'base'}-${index}`}
      columns={[
        { key: 'checkType', label: t('Check Type'), required: true, cellClassName: 'cell-nowrap', render: ({ command }) => command.checkType },
        { key: 'environment', label: t('Environment'), cellClassName: 'text-dim cell-nowrap', render: ({ command }) => command.environmentName || t('Base') },
        { key: 'command', label: t('Command'), required: true, render: ({ command }) => <code className="workflow-command-preview-code">{command.command}</code> },
      ]}
    />
  );
}
