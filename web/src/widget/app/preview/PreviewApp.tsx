import { useCallback, useEffect, useLayoutEffect, useMemo, useState } from 'react';
import { createPortal, flushSync } from 'react-dom';
import { createLauncherView, type LauncherView } from '../../loader/launcher';
import { EDIT_ATTRIBUTE, type ColorScheme, type EditMark, type EditTarget, type PreviewScene } from '../../shared/preview';
import { ChatPanel } from '../ChatPanel';
import { postToEditor, readEditorMessage } from '../host';
import { paintTheme, resolveFor } from '../theme';
import { normalizeConfig, type WidgetConfig } from '../types';
import { useWindowEvent } from '../useWindowEvent';
import { WidgetProvider } from '../WidgetProvider';
import { LiveChatPreview, useLiveSession } from './LivePreview';
import { PreviewPicker } from './picker';
import { SamplePage } from './SamplePage';
import { SceneSession, sceneConfig, sceneState, type ChatScene } from './scenes';

// /widget/v1/preview: the widget on a sample page for the portal's editor. It only shows what the editor sends and
// never calls the public API, except in live mode ("Test this widget").

interface Shown {
  config: WidgetConfig;
  scene: PreviewScene;
  colorScheme: ColorScheme;
}

interface Stage {
  launcher: LauncherView;
  // A light DOM child of the launcher's host, slotted into its panel where host pages show the frame.
  chat: HTMLElement;
  picker: PreviewPicker;
}

function mark(target: EditTarget): EditMark {
  return { [EDIT_ATTRIBUTE]: target };
}

function stayOpen(): void {
  // The preview has nothing to close the panel for.
}

// Keyed by the scene, so each scene starts from its own sample conversation.
function SceneChat({ config, scene, onBusy }: { config: WidgetConfig; scene: ChatScene; onBusy: (busy: boolean) => void }) {
  const [session] = useState(() => new SceneSession(sceneState(scene)));
  // A new config object would change the context and render the whole chat again.
  const shown = useMemo(() => sceneConfig(config, scene), [config, scene]);
  return (
    <WidgetProvider config={shown} mark={mark}>
      <ChatPanel session={session} onClose={stayOpen} onBusy={onBusy} />
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
        // Render synchronously, so the next report and the editor's next message see the new state.
        flushSync(() => setShown({ config, scene: message.scene, colorScheme: message.colorScheme }));
        stage.picker.rendered(message.scene);
      }
    } else if (message?.type === 'track') {
      stage.picker.track(message.target, message.fallbacks);
    } else if (message?.type === 'live') {
      setLive({ widgetId: message.widgetId, colorScheme: message.colorScheme });
    }
  });

  // Messages from the editor are handled once the stage is there, which the editor waits for.
  useEffect(() => {
    if (stage) {
      postToEditor({ type: 'ready' });
    }
  }, [stage]);

  // Live, the launcher opens and closes the panel as on a visitor's site, and nothing is picked for editing.
  useEffect(() => {
    if (!stage || !liveWidget) {
      return undefined;
    }
    stage.picker.dispose();
    stage.launcher.button.removeAttribute(EDIT_ATTRIBUTE);
    stage.launcher.setOpen(true);
    // Read from the launcher, as the chat's own close button closes it too.
    const toggle = () => stage.launcher.setOpen(!stage.launcher.root.classList.contains('ka-is-open'));
    stage.launcher.button.addEventListener('click', toggle);
    return () => stage.launcher.button.removeEventListener('click', toggle);
  }, [stage, liveWidget]);

  const liveConfig = live && liveSession.state === 'ready' ? liveSession.config : null;
  const config = live ? liveConfig : (shown?.config ?? null);
  const scene = live ? null : (shown?.scene ?? null);
  const colorScheme = live?.colorScheme ?? shown?.colorScheme ?? 'light';
  // "auto" follows the visitor's system, which the editor's light and dark switch stands for.
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
      {stage && config && scene && scene !== 'launcher'
        ? createPortal(<SceneChat key={scene} config={config} scene={scene} onBusy={setBusy} />, stage.chat)
        : null}
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
