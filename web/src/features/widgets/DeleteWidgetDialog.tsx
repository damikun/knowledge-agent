import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ErrorMessage } from '../../components/ui/feedback';
import { ConfirmDialog } from '../../components/ui/Modal';
import { toast } from '../../components/ui/Toast';
import { widgetKeys, widgetsApi } from './api';
import type { WidgetDto } from './types';

export interface DeleteWidgetDialogProps {
  widget: WidgetDto | null;
  onClose: () => void;
  onDeleted?: () => void;
}

export function DeleteWidgetDialog({ widget, onClose, onDeleted }: DeleteWidgetDialogProps) {
  const queryClient = useQueryClient();
  const remove = useMutation({
    mutationFn: (id: string) => widgetsApi.deleteWidget(id),
    onSuccess: (_result, id) => {
      queryClient.setQueryData<WidgetDto[]>(widgetKeys.list, (old) => old?.filter((item) => item.id !== id));
      void queryClient.invalidateQueries({ queryKey: widgetKeys.list });
      toast({ title: 'Widget deleted', tone: 'success' });
      onClose();
      onDeleted?.();
    },
  });

  return (
    <ConfirmDialog
      open={widget !== null}
      title="Delete widget?"
      message={
        <>
          “{widget?.name}” will be deleted with its images. Sites that embed it stop showing the chat. Its conversations stay until they expire. This
          cannot be undone.
        </>
      }
      confirmLabel="Delete"
      busy={remove.isPending}
      error={remove.isError ? <ErrorMessage error={remove.error} /> : null}
      onConfirm={() => widget && remove.mutate(widget.id)}
      onCancel={() => {
        remove.reset();
        onClose();
      }}
    />
  );
}
