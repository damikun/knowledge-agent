import { useCallback, useEffect, useEffectEvent, useRef, useState } from 'react';
import { pathOf } from '../../../lib/endpoint';
import { endpoints } from '../../../lib/endpoints';
import {
  envelope,
  parsePreviewMessage,
  type ColorScheme,
  type EditorMessage,
  type EditTarget,
  type PreviewRect,
  type PreviewScene,
} from '../../../widget/shared/preview';
import type { WidgetPreviewConfig } from '../types';

export const PREVIEW_URL = pathOf(endpoints.widgets.preview);

interface Pointed {
  target: EditTarget;
  rect: PreviewRect;
}

export interface Tracking {
  target: EditTarget;
  fallbacks: readonly EditTarget[];
}

export interface PreviewBridgeOptions {
  frame: HTMLIFrameElement | null;
  config: WidgetPreviewConfig;
  scene: PreviewScene;
  colorScheme: ColorScheme;
  tracking: Tracking | null;
  onPick: (target: EditTarget | null) => void;
}

export interface PreviewBridge {
  shown: ReadonlySet<EditTarget> | null;
  hovered: Pointed | null;
  anchor: Pointed | null;
  painted: boolean;
}

export function usePreviewBridge({ frame, config, scene, colorScheme, tracking, onPick }: PreviewBridgeOptions): PreviewBridge {
  const [connection, setConnection] = useState(0);
  const [rendered, setRendered] = useState<{ scene: PreviewScene; targets: ReadonlySet<EditTarget> } | null>(null);
  const [hovered, setHovered] = useState<Pointed | null>(null);
  const [placed, setPlaced] = useState<{ target: EditTarget; anchor: Pointed } | null>(null);
  const requested = useRef<EditTarget | null>(null);
  const pick = useEffectEvent(onPick);

  useEffect(() => {
    const preview = frame?.contentWindow;
    if (!preview) {
      return undefined;
    }
    const onMessage = (event: MessageEvent) => {
      if (event.origin !== window.location.origin || event.source !== preview) {
        return;
      }
      const message = parsePreviewMessage(event.data);
      switch (message?.type) {
        case 'ready':
          setRendered(null);
          setHovered(null);
          setPlaced(null);
          setConnection((count) => count + 1);
          break;
        case 'rendered':
          setRendered({ scene: message.scene, targets: new Set(message.targets) });
          break;
        case 'hover':
          setHovered(message.target && message.rect ? { target: message.target, rect: message.rect } : null);
          break;
        case 'select':
          if (message.target && message.rect) {
            requested.current = message.target;
            setPlaced({ target: message.target, anchor: { target: message.target, rect: message.rect } });
          }
          pick(message.target);
          break;
        case 'rect':
          if (message.target === requested.current) {
            setPlaced(message.anchor && message.rect ? { target: message.target, anchor: { target: message.anchor, rect: message.rect } } : null);
          }
          break;
        default:
          break;
      }
    };
    window.addEventListener('message', onMessage);
    return () => window.removeEventListener('message', onMessage);
  }, [frame]);

  const post = useCallback((message: EditorMessage) => frame?.contentWindow?.postMessage(envelope(message), window.location.origin), [frame]);

  useEffect(() => {
    if (connection > 0) {
      post({ type: 'render', config, scene, colorScheme });
    }
  }, [connection, post, config, scene, colorScheme]);

  useEffect(() => {
    requested.current = tracking?.target ?? null;
    if (connection > 0) {
      post({ type: 'track', target: tracking?.target ?? null, fallbacks: tracking ? [...tracking.fallbacks] : [] });
    }
  }, [connection, post, tracking]);

  return {
    shown: rendered?.scene === scene ? rendered.targets : null,
    hovered,
    anchor: tracking && placed?.target === tracking.target ? placed.anchor : null,
    painted: rendered !== null,
  };
}
