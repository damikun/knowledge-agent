import { useCallback, useEffect, useLayoutEffect, useMemo, useState } from 'react';
import { createPortal, flushSync } from 'react-dom';
import { createLauncherView, type LauncherView } from '../../loader/launcher';
import { EDIT_ATTRIBUTE, type ColorScheme, type EditMark, type EditTarget, type PreviewScene } from '../../shared/preview';
import { ChatPanel } from '../ChatPanel';
import { postToEditor, readEditorMessage } from '../host';
import { paintTheme, resolveFor } from '../theme';
import { normalizeConfig, type WidgetConfig } from '../types';
import { useWindowEvent } from '../useWindowEvent';
import { readyConfig, type Widget } from '../widgetRuntime';
import { noHost } from '../hostLink';
import { isBusy } from '../chatState';
import { WidgetProvider } from '../WidgetProvider';
import { LiveChatPreview, useLiveSession } from './LivePreview';
import { PreviewPicker } from './picker';
import { SamplePage } from './SamplePage';
import { sceneChat, sceneConfig, sceneState, sceneVisitor, type ChatScene } from './scenes';

interface Shown {
  config: WidgetConfig;
  scene: PreviewScene;
  colorScheme: ColorScheme;
}

interface Stage {
  launcher: LauncherView;
  chat: HTMLElement;
  picker: PreviewPicker;
}

function mark(target: EditTarget): EditMark {
  return { [EDIT_ATTRIBUTE]: target };
}

function SceneChat({ config, scene }: { config: WidgetConfig; scene: ChatScene }) {
  const [chat] = useState(() => sceneChat(sceneState(scene)));
  const [visitor] = useState(() => sceneVisitor(scene));
  const shown = useMemo(() => sceneConfig(config, scene), [config, scene]);
  const widget = useMemo<Widget>(() => ({ config: readyConfig(shown), visitor, chat, host: noHost }), [shown, visitor, chat]);
  return (
    <WidgetProvider widget={widget} mark={mark}>
      <ChatPanel />
    </WidgetProvider>
  );
}

interface Live {
  widgetId: string;
  colorScheme: ColorScheme;
}

export function PreviewApp() {
  const [stage, setStage] = useState<Stage | null>(null);
  const [shown, setShown] = useState<Shown | null>(null);
  const [live, setLive] = useState<Live | null>(null);
  const liveWidget = live?.widgetId ?? null;
  const liveSession = useLiveSession(liveWidget);

  const mountStage = useCallback((container: HTMLDivElement) => {
    const launcher = createLauncherView(window.location.origin);
    launcher.button.setAttribute(EDIT_ATTRIBUTE, 'appearance.launcher');
    launcher.panel.append(document.createElement('slot'));
    const chat = document.createElement('div');
    chat.setAttribute('data-ka-chat', '');
    launcher.host.append(chat);
    container.append(launcher.host);
    const roots = [document, launcher.host.shadowRoot].filter((root): root is Document | ShadowRoot => root !== null);
    const picker = new PreviewPicker(roots, postToEditor);
    setStage({ launcher, chat, picker });
    return () => {
      picker.dispose();
      launcher.host.remove();
    };
  }, []);

  useWindowEvent('message', (event) => {
    if (!stage) {
      return;
    }
    const message = readEditorMessage(event);
    if (message?.type === 'render') {
      const config = normalizeConfig(message.config);
      if (config) {
        flushSync(() => setShown({ config, scene: message.scene, colorScheme: message.colorScheme }));
        stage.picker.rendered(message.scene);
      }
    } else if (message?.type === 'track') {
      stage.picker.track(message.target, message.fallbacks);
    } else if (message?.type === 'live') {
      setLive({ widgetId: message.widgetId, colorScheme: message.colorScheme });
    }
  });

  useEffect(() => {
    if (stage) {
      postToEditor({ type: 'ready' });
    }
  }, [stage]);

  useEffect(() => {
    if (!stage || !liveWidget) {
      return undefined;
    }
    stage.picker.dispose();
    stage.launcher.button.removeAttribute(EDIT_ATTRIBUTE);
    stage.launcher.setOpen(true);
    const toggle = () => stage.launcher.setOpen(!stage.launcher.root.classList.contains('ka-is-open'));
    stage.launcher.button.addEventListener('click', toggle);
    return () => stage.launcher.button.removeEventListener('click', toggle);
  }, [stage, liveWidget]);

  const liveConfig = live && liveSession.state === 'ready' ? liveSession.config : null;
  const config = live ? liveConfig : (shown?.config ?? null);
  const scene = live ? null : (shown?.scene ?? null);
  const colorScheme = live?.colorScheme ?? shown?.colorScheme ?? 'light';
  const mode = config ? (config.theme === 'auto' ? colorScheme : config.theme) : null;

  useLayoutEffect(() => {
    if (stage && config && mode) {
      stage.launcher.render(config, mode);
      paintTheme(resolveFor(config, mode));
    }
  }, [stage, config, mode]);

  useLayoutEffect(() => {
    if (stage && scene) {
      stage.launcher.setOpen(scene !== 'launcher');
      stage.launcher.setBusy(scene !== 'launcher' && isBusy(sceneState(scene)));
    }
  }, [stage, scene]);

  const setBusy = (busy: boolean) => stage?.launcher.setBusy(busy);
  const closeLive = () => {
    stage?.launcher.setOpen(false);
    stage?.launcher.button.focus();
  };

  return (
    <div
      data-scheme={colorScheme}
      className="fixed inset-0 overflow-hidden bg-zinc-100 text-zinc-900 data-[scheme=dark]:bg-zinc-950 data-[scheme=dark]:text-zinc-100"
    >
      <SamplePage />
      <div ref={mountStage} />
      {stage && config && scene && scene !== 'launcher' ? createPortal(<SceneChat key={scene} config={config} scene={scene} />, stage.chat) : null}
      {stage && live && liveSession.state === 'ready'
        ? createPortal(
            <LiveChatPreview widgetId={live.widgetId} config={liveSession.config} first={liveSession.first} onClose={closeLive} onBusy={setBusy} />,
            stage.chat,
          )
        : null}
      {live && liveSession.state === 'failed' ? (
        <p role="alert" className="absolute inset-x-6 top-6 mx-auto max-w-md rounded-2xl bg-white/90 px-4 py-3 text-sm text-zinc-900 shadow-lg">
          The preview could not start. {liveSession.message}
        </p>
      ) : null}
    </div>
  );
}
