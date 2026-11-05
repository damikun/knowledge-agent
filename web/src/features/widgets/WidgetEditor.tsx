import { useMutation, useQueryClient } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { useEffect, useId, useMemo, useState } from 'react';
import { flushSync } from 'react-dom';
import { useLocation, useNavigate } from 'react-router';
import { Alert, ErrorMessage } from '../../components/ui/feedback';
import { Page } from '../../components/ui/Page';
import { ApiError } from '../../lib/api';
import { useBreadcrumb } from '../../lib/breadcrumb';
import { isHexColor } from '../../lib/color';
import { useElementSize } from '../../lib/hooks/useElementSize';
import { useKeydown } from '../../lib/hooks/useKeydown';
import type { ColorScheme, EditTarget, PreviewScene } from '../../widget/shared/preview';
import { resolveTheme } from '../../widget/shared/theme';
import type { AgentDto } from '../agents/types';
import type { IntegrationsDto } from '../notifications/types';
import { widgetKeys, widgetsApi } from './api';
import { DeleteWidgetDialog } from './DeleteWidgetDialog';
import { WidgetPreviewDialog } from './WidgetPreviewDialog';
import { BottomSheet, SHEET_HANDLE_HEIGHT, type SheetSnap } from './editor/BottomSheet';
import type { EditorKit } from './editor/controls';
import { EditorHeader } from './editor/EditorHeader';
import { ERROR_ORDER, GROUP_FIELDS, TEXTS_FIELD, useEditorErrors } from './editor/useEditorErrors';
import { useWidgetDraft, type DraftPath, type DraftValue } from './editor/draft';
import { PreviewCanvas, type Device } from './editor/PreviewCanvas';
import { buildRegistry, controlIdOf } from './editor/registry';
import { SidePanel, type PanelId, type PanelTab } from './editor/SidePanel';
import { usePreviewBridge, type Tracking } from './editor/usePreviewBridge';
import type { SaveWidgetRequest, WidgetDto, WidgetOptionsDto } from './types';
import {
  initialWidgetValues,
  toPreviewConfig,
  toSaveRequest,
  type ColorMode,
  type WidgetField,
  type WidgetFormValues,
} from './widgetForm';
import { readReturnTo } from '../../lib/returnTo';

export interface WidgetEditorProps {
  widget: WidgetDto;
  options: WidgetOptionsDto;
  agents: readonly AgentDto[];
  integrations: IntegrationsDto | undefined;
}

// Inside a text field, undo is the field's own.
function isTyping(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }
  if (target instanceof HTMLTextAreaElement || target.isContentEditable) {
    return true;
  }
  return target instanceof HTMLInputElement && !['checkbox', 'radio', 'button', 'submit', 'color', 'file'].includes(target.type);
}

// Room for the preview beside a 360 px panel; narrower workspaces get a bottom sheet.
const WIDE_WORKSPACE = 900;
// Estimated height of the sheet's search and tabs until they are measured.
const SHEET_HEADER_HEIGHT = 96;

export function WidgetEditor({ widget, options, agents, integrations }: WidgetEditorProps) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const location = useLocation();
  const prefix = useId();
  const savedSettings = widget.settings;
  const registry = useMemo(() => buildRegistry(options), [options]);
  useBreadcrumb(widget.name);
  const draft = useWidgetDraft(() => initialWidgetValues(widget, options.defaults));
  const { values, dispatch, set: setDraft } = draft;

  const [frame, setFrame] = useState<HTMLIFrameElement | null>(null);
  const [scene, setScene] = useState<PreviewScene>('start');
  const [colorScheme, setColorScheme] = useState<ColorScheme>(() => (document.documentElement.classList.contains('dark') ? 'dark' : 'light'));
  const [device, setDevice] = useState<Device>('desktop');
  const [selected, setSelected] = useState<EditTarget | null>(null);
  // A counter, so picking the same element again still resets its view.
  const [reveal, setReveal] = useState<{ count: number; focusId: string | null }>({ count: 0, focusId: null });
  const [panels, setPanels] = useState<ReadonlySet<PanelId>>(() => new Set<PanelId>(['embed']));
  const [attempted, setAttempted] = useState(false);
  const [submitted, setSubmitted] = useState<WidgetFormValues | null>(null);
  const [notice, setNotice] = useState<string | null>(() => (location.state as { notice?: string } | null)?.notice ?? null);
  const [tab, setTab] = useState<PanelTab>(notice ? 'settings' : 'elements');
  const [confirmDelete, setConfirmDelete] = useState(false);
  // The Playground links here with ?preview.
  const [previewOpen, setPreviewOpen] = useState(() => new URLSearchParams(location.search).has('preview'));
  const [workspace, setWorkspace] = useState<HTMLDivElement | null>(null);
  const [sheetHeader, setSheetHeader] = useState<HTMLDivElement | null>(null);
  const workspaceSize = useElementSize(workspace);
  const sheetHeaderSize = useElementSize(sheetHeader);
  // Nothing to measure (jsdom): treat the workspace as wide.
  const wide = workspaceSize === null || workspaceSize.width >= WIDE_WORKSPACE;
  const [sheet, setSheet] = useState<SheetSnap>(notice ? 'half' : 'collapsed');
  const peek = SHEET_HANDLE_HEIGHT + (sheetHeaderSize?.height ?? SHEET_HEADER_HEIGHT);

  const save = useMutation({
    mutationFn: (body: SaveWidgetRequest) => widgetsApi.updateWidget(widget.id, body),
    onSuccess: (saved) => {
      queryClient.setQueryData<WidgetDto[]>(widgetKeys.list, (old) => old?.map((item) => (item.id === saved.id ? saved : item)));
      void queryClient.invalidateQueries({ queryKey: widgetKeys.list });
      dispatch({ type: 'saved', values: initialWidgetValues(saved, options.defaults) });
      setAttempted(false);
      setNotice('Widget saved.');
    },
    onError: (error) => {
      const field = error instanceof ApiError ? ERROR_ORDER.find((candidate) => error.errorsFor(candidate).length > 0) : undefined;
      if (field) {
        revealField(field);
      }
    },
  });

  // The notice comes in history state; clear it so a reload doesn't show it again.
  useEffect(() => {
    if ((location.state as { notice?: string } | null)?.notice) {
      // Drop the notice but keep the way back.
      const returnTo = readReturnTo(location.state);
      navigate(location.pathname, { replace: true, state: returnTo ? { returnTo } : null });
    }
  }, [location.state, location.pathname, navigate]);

  const { validation, errorsFor, otherErrors, propertyOf, problems, errorCount } = useEditorErrors({
    registry,
    values,
    attempted,
    submitted,
    error: save.error,
  });

  const set = <P extends DraftPath>(path: P, value: DraftValue<P>) => {
    setDraft(path, value);
    setNotice(null);
  };

  const previewConfig = useMemo(() => toPreviewConfig(values, widget.publicKey, savedSettings), [values, widget, savedSettings]);
  const mode: ColorMode = values.theme === 'light' || values.theme === 'dark' ? values.theme : colorScheme;
  const theme = useMemo(
    () => resolveTheme({ preset: previewConfig.preset, brandColor: previewConfig.brandColor, mode, overrides: previewConfig.colors[mode] }),
    [previewConfig, mode],
  );
  const kit: EditorKit = {
    values,
    saved: draft.saved,
    set,
    errorsFor,
    mode,
    theme,
    id: (name) => `${prefix}-${name}`,
    agents,
    integrations,
    widgetId: widget.id,
  };

  const tracking = useMemo<Tracking | null>(() => (selected ? { target: selected, fallbacks: registry.fallbacks(selected) } : null), [selected, registry]);
  const pick = (target: EditTarget | null, focusId: string | null = null) => {
    dispatch({ type: 'checkpoint' });
    setSelected(target);
    setReveal((current) => ({ count: current.count + 1, focusId }));
    if (target) {
      setTab('elements');
      setSheet((current) => (current === 'collapsed' ? 'half' : current));
    }
  };
  const preview = usePreviewBridge({ frame, config: previewConfig, scene, colorScheme, tracking, onPick: pick });

  const select = (target: EditTarget, focusId: string | null = null) => {
    const definition = registry.target(target);
    if (scene !== definition.scene && !preview.shown?.has(target)) {
      setScene(definition.scene);
    }
    pick(target, focusId);
  };

  const togglePanel = (id: PanelId) =>
    setPanels((current) => {
      const next = new Set(current);
      if (!next.delete(id)) {
        next.add(id);
      }
      return next;
    });

  const expandSheet = () => setSheet((current) => (current === 'collapsed' ? 'half' : current));

  const changeSheet = (next: SheetSnap) => {
    setSheet(next);
    if (next === 'collapsed' && selected) {
      pick(null);
    }
  };

  // Render the section synchronously so its field can take focus.
  const showInPanel = (panelTab: PanelTab, section: PanelId | null, focusId: string | null) => {
    flushSync(() => {
      pick(null);
      expandSheet();
      setTab(panelTab);
      if (section) {
        setPanels((current) => new Set(current).add(section));
      }
    });
    if (focusId) {
      document.getElementById(focusId)?.focus();
    }
  };

  const revealProperty = (id: DraftPath) => {
    const place = registry.locate(id);
    const focusId = controlIdOf(kit.id, id);
    if (place?.kind === 'target') {
      select(place.target, focusId);
    } else if (place?.section === 'theme') {
      showInPanel('theme', null, focusId);
    } else if (place) {
      showInPanel('settings', place.section, focusId);
    }
  };

  const revealField = (field: WidgetField) => {
    const group = GROUP_FIELDS[field];
    const property = propertyOf(field);
    if (group?.mode) {
      const invalid = Object.entries(values.colors[group.mode]).find(([, color]) => color.trim() !== '' && !isHexColor(color));
      if (values.theme === 'auto') {
        setColorScheme(group.mode);
      }
      showInPanel('theme', null, invalid ? controlIdOf(kit.id, `colors.${group.mode}.${invalid[0]}`) : null);
    } else if (group) {
      showInPanel('settings', 'texts', null);
    } else if (property) {
      revealProperty(property.id);
    }
  };

  const submit = () => {
    dispatch({ type: 'checkpoint' });
    const { request, errors } = toSaveRequest(values, savedSettings);
    setAttempted(true);
    setNotice(null);
    if (request) {
      setSubmitted(values);
      save.mutate(request);
    } else {
      const first = ERROR_ORDER.find((field) => errors[field]);
      if (first) {
        revealField(first);
      }
    }
  };
  const canSave = draft.dirty && !save.isPending;

  useKeydown((event) => {
    const key = event.key.toLowerCase();
    if (!(event.metaKey || event.ctrlKey) || event.altKey) {
      return;
    }
    // Ctrl+S works from any field; undo inside a field stays native.
    if (key === 's') {
      event.preventDefault();
      if (canSave) {
        submit();
      }
    } else if ((key === 'z' || key === 'y') && !isTyping(event.target)) {
      event.preventDefault();
      dispatch({ type: key === 'y' || event.shiftKey ? 'redo' : 'undo' });
    }
  });

  const target = selected ? registry.target(selected) : null;
  const failedTitle = 'The widget was not saved';
  const panelProps = {
    registry,
    kit,
    scene,
    tab,
    onTabChange: setTab,
    open: panels,
    onToggle: togglePanel,
    selected,
    shown: preview.shown,
    onSelect: (id: EditTarget) => select(id),
    errorCount,
    textErrors: errorsFor(TEXTS_FIELD),
    onResetColors: () => dispatch({ type: 'resetColors', mode }),
    publicKey: widget.publicKey,
    enabled: values.enabled,
    identity: { widgetId: widget.id, enabled: widget.identityVerification, createdAt: widget.identitySecretCreatedAt },
    notShown: preview.anchor !== null && target !== null && preview.anchor.target !== target.id,
    focusId: reveal.focusId,
    revealCount: reveal.count,
    onBack: () => pick(null),
    onOpenTheme: () => showInPanel('theme', null, null),
    onShowLauncher: () => setScene('launcher'),
  };

  return (
    <Page variant="workspace">
      <EditorHeader
        widget={widget}
        notice={notice}
        dirty={draft.dirty}
        problems={problems}
        onReveal={revealField}
        canUndo={draft.canUndo}
        canRedo={draft.canRedo}
        onUndo={() => dispatch({ type: 'undo' })}
        onRedo={() => dispatch({ type: 'redo' })}
        onDelete={() => setConfirmDelete(true)}
        onTest={() => setPreviewOpen(true)}
        onDiscard={() => dispatch({ type: 'discard' })}
        saving={save.isPending}
        canSave={canSave}
        onSave={submit}
      />
      {(save.isError && !validation) || otherErrors.length > 0 ? (
        <div className="shrink-0 px-4 pb-2 lg:px-6">
          {save.isError && !validation ? <ErrorMessage error={save.error} title={failedTitle} /> : null}
          {otherErrors.length > 0 ? (
            <Alert tone="danger" title={failedTitle} live>
              {otherErrors.join(' ')}
            </Alert>
          ) : null}
        </div>
      ) : null}

      <div
        ref={setWorkspace}
        className={clsx('relative min-h-0 flex-1', wide && 'grid grid-cols-[minmax(0,1fr)_22.5rem] grid-rows-[minmax(0,1fr)] gap-3 px-4 pb-4 lg:px-6')}
      >
        {/* One element in both layouts, so the preview iframe stays loaded across resizes. */}
        <div
          className={wide ? 'relative min-h-0' : 'absolute inset-x-0 top-0 px-4 lg:px-6'}
          style={wide ? undefined : { bottom: sheet === 'collapsed' && !target ? peek + 8 : 'calc(50% + 8px)' }}
        >
          <PreviewCanvas
            frameRef={setFrame}
            scene={scene}
            onSceneChange={setScene}
            colorScheme={colorScheme}
            onColorSchemeChange={setColorScheme}
            device={device}
            onDeviceChange={setDevice}
            hovered={preview.hovered ? { rect: preview.hovered.rect, label: registry.target(preview.hovered.target).label } : null}
            selected={target && preview.anchor ? { rect: preview.anchor.rect, label: target.label } : null}
            painted={preview.painted}
          />
        </div>
        {wide ? (
          <SidePanel {...panelProps} />
        ) : (
          <BottomSheet label="Settings" snap={target && sheet === 'collapsed' ? 'half' : sheet} onSnapChange={changeSheet} peek={peek}>
            <SidePanel {...panelProps} variant="sheet" headerRef={setSheetHeader} contentInert={sheet === 'collapsed'} onExpand={expandSheet} />
          </BottomSheet>
        )}
      </div>
      <WidgetPreviewDialog widget={previewOpen ? widget : null} unsaved={draft.dirty} onClose={() => setPreviewOpen(false)} />
      <DeleteWidgetDialog
        widget={confirmDelete ? widget : null}
        onClose={() => setConfirmDelete(false)}
        onDeleted={() => navigate('/widgets', { replace: true })}
      />
    </Page>
  );
}
