import { ArrowUp, MessageCircle } from 'lucide-react';
import { useLayoutEffect, useMemo, useRef, useState, useSyncExternalStore, type CSSProperties, type RefObject } from 'react';
import { isHexColor } from '../../lib/color';
import { readThemeColors, resolveTheme, TRANSPARENT, type ColorMode } from '../../widget/shared/theme';
import { FALLBACK_SETTINGS, mediaUrl } from './api';
import type { WidgetAppearance, WidgetBehavior } from './types';
import { LAUNCHER_ICONS } from './widgetTexts';

function subscribeScheme(onChange: () => void): () => void {
  if (typeof MutationObserver === 'undefined') {
    return () => undefined;
  }
  const observer = new MutationObserver(onChange);
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] });
  return () => observer.disconnect();
}

function portalScheme(): ColorMode {
  return document.documentElement.classList.contains('dark') ? 'dark' : 'light';
}

function usePortalScheme(): ColorMode {
  return useSyncExternalStore(subscribeScheme, portalScheme, () => 'light');
}

// A sample page behind the widget, so glass presets have something to show through.
const SAMPLE_PAGE: Readonly<Record<ColorMode, { background: string; ink: string }>> = {
  light: { background: '#f4f4f5', ink: '#18181b' },
  dark: { background: '#09090b', ink: '#f4f4f5' },
};
const BLOBS = [
  { top: -160, side: -60, size: 560, color: 'rgb(99 102 241 / 0.5)' },
  { top: 300, side: 300, size: 440, color: 'rgb(236 72 153 / 0.35)' },
  { top: 80, side: 150, size: 340, color: 'rgb(20 184 166 / 0.4)' },
];

// Drawn at a 4:3 browser window size and scaled down whole, so the 360 x 560 panel keeps its proportions.
const STAGE = { width: 896, height: 672 } as const;
const PANEL = { width: 360, height: 560 } as const;
const EDGE = 24;
const LAUNCHER = 56;
const GAP = 16;
// Until the card is measured (and in tests): about one card in a three-column grid.
const DEFAULT_SCALE = 0.4;

function initialOf(text: string): string {
  return text.trim().charAt(0).toUpperCase() || '?';
}

function useStageScale(): [RefObject<HTMLDivElement | null>, number] {
  const ref = useRef<HTMLDivElement>(null);
  const [scale, setScale] = useState(DEFAULT_SCALE);
  // Layout effect, so the still never paints at the wrong size.
  useLayoutEffect(() => {
    const element = ref.current;
    if (!element) {
      return undefined;
    }
    const measure = (width: number, height: number) => {
      if (width > 0 && height > 0) {
        setScale(Math.min(width / STAGE.width, height / STAGE.height));
      }
    };
    measure(element.clientWidth, element.clientHeight);
    if (typeof ResizeObserver === 'undefined') {
      return undefined;
    }
    const observer = new ResizeObserver(([entry]) => measure(entry?.contentRect.width ?? 0, entry?.contentRect.height ?? 0));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  return [ref, scale];
}

export interface WidgetThumbnailProps {
  appearance: WidgetAppearance;
  greeting: WidgetBehavior['greeting'];
  suggestedQuestions?: WidgetBehavior['suggestedQuestions'];
  placeholder?: WidgetBehavior['placeholder'];
  className?: string;
}

export function WidgetThumbnail({ appearance, greeting, suggestedQuestions = [], placeholder = null, className }: WidgetThumbnailProps) {
  const portal = usePortalScheme();
  const [boxRef, scale] = useStageScale();
  const mode: ColorMode = appearance.theme === 'light' || appearance.theme === 'dark' ? appearance.theme : portal;
  const brandColor = isHexColor(appearance.brandColor) ? appearance.brandColor : FALLBACK_SETTINGS.appearance.brandColor;
  const theme = useMemo(
    () => resolveTheme({ preset: appearance.preset, brandColor, mode, overrides: readThemeColors(appearance.colors)[mode] }),
    [appearance.preset, appearance.colors, brandColor, mode],
  );
  const { colors, variables, effects } = theme;
  const page = SAMPLE_PAGE[mode];
  const left = appearance.position === 'left';
  const near = left ? 'left' : 'right';
  const far = left ? 'right' : 'left';
  const Icon = LAUNCHER_ICONS[appearance.launcherIcon]?.icon ?? MessageCircle;
  const questions = suggestedQuestions.filter((question) => question.trim() !== '').slice(0, 2);

  const panel: CSSProperties = {
    [near]: EDGE,
    top: STAGE.height - EDGE - LAUNCHER - GAP - PANEL.height,
    width: PANEL.width,
    height: PANEL.height,
    borderRadius: effects.radius,
    background: effects.glass ? variables['--ka-panel-fill'] : colors.panel,
    border: `1px solid ${colors.border}`,
    color: colors.text,
    boxShadow: mode === 'dark' ? '0 24px 60px -20px rgb(0 0 0 / 0.8)' : '0 24px 60px -24px rgb(0 0 0 / 0.35)',
    backdropFilter: effects.glass ? 'blur(18px) saturate(1.4)' : undefined,
  };

  return (
    <div
      ref={boxRef}
      aria-hidden
      data-preset={theme.preset}
      data-mode={mode}
      className={className}
      style={{ position: 'relative', overflow: 'hidden', aspectRatio: '4 / 3', background: page.background, color: page.ink }}
    >
      <div
        data-thumbnail-stage
        className="absolute top-1/2 left-1/2"
        style={{ width: STAGE.width, height: STAGE.height, transform: `translate(-50%, -50%) scale(${scale})` }}
      >
        {BLOBS.map((blob, index) => (
          <span
            key={index}
            className="absolute rounded-full blur-3xl"
            style={{
              top: blob.top,
              [near]: blob.side,
              width: blob.size,
              height: blob.size,
              background: `radial-gradient(circle, ${blob.color}, transparent 65%)`,
            }}
          />
        ))}
        <div className="absolute top-12 space-y-5" style={{ [far]: 48, width: 380 }}>
          <span className="block h-6 w-28 rounded-md bg-current opacity-20" />
          <span className="block h-9 w-4/5 rounded-xl bg-current opacity-20" />
          <span className="block h-9 w-3/5 rounded-xl bg-current opacity-20" />
          <span className="block h-3.5 w-full rounded-full bg-current opacity-10" />
          <span className="block h-3.5 w-5/6 rounded-full bg-current opacity-10" />
          <span className="block h-10 w-36 rounded-full bg-current opacity-25" />
        </div>

        <div className="absolute flex flex-col overflow-hidden" style={panel}>
          <div
            data-thumbnail-header
            className="flex shrink-0 items-center gap-3 px-5 py-4"
            style={{ background: colors.header === TRANSPARENT ? undefined : colors.header, color: colors.headerText }}
          >
            {appearance.avatarAssetId ? (
              <img src={mediaUrl(appearance.avatarAssetId)} alt="" className="size-9 shrink-0 rounded-full object-cover" />
            ) : (
              <span
                className="grid size-9 shrink-0 place-items-center rounded-full text-base font-semibold"
                style={{ background: `color-mix(in oklab, ${colors.headerText} 12%, transparent)` }}
              >
                {initialOf(appearance.title)}
              </span>
            )}
            <span className="min-w-0">
              <span className="block truncate text-base leading-tight font-semibold">{appearance.title}</span>
              {appearance.subtitle ? <span className="block truncate text-[13px] opacity-75">{appearance.subtitle}</span> : null}
            </span>
          </div>
          <div className="flex min-h-0 flex-1 flex-col gap-3 overflow-hidden px-5 pt-3">
            {greeting ? (
              <p
                className="line-clamp-4 w-fit max-w-[85%] shrink-0 rounded-2xl rounded-bl-md px-4 py-2.5 text-[15px] leading-normal"
                style={{ background: variables['--ka-assistant-bubble'], color: colors.assistantBubbleText }}
              >
                {greeting}
              </p>
            ) : null}
            {questions.length > 0 ? (
              <div className="flex shrink-0 flex-col items-start gap-2">
                {questions.map((question, index) => (
                  <span
                    key={index}
                    className="max-w-[85%] truncate rounded-full px-3.5 py-1.5 text-[13px] font-medium"
                    style={{ border: `1px solid ${colors.border}`, color: variables['--ka-accent-text'] }}
                  >
                    {question}
                  </span>
                ))}
              </div>
            ) : null}
            <span
              className="ml-auto block h-10 w-44 shrink-0 rounded-2xl rounded-br-md"
              style={{ background: `linear-gradient(135deg, ${colors.userBubble}, ${variables['--ka-user-bubble-end']})` }}
            />
          </div>
          <div className="shrink-0 px-4 pt-2 pb-3">
            <div className="flex h-12 items-center gap-2 rounded-full pr-1.5 pl-4" style={{ border: `1px solid ${variables['--ka-control']}` }}>
              <span className="flex-1 truncate text-[15px]" style={{ color: colors.muted }}>
                {placeholder?.trim() || 'Ask a question'}
              </span>
              <span
                className="grid size-9 place-items-center rounded-full"
                style={{ background: variables['--ka-accent'], color: variables['--ka-on-accent'] }}
              >
                <ArrowUp className="size-4" />
              </span>
            </div>
            {appearance.showFooter ? <p className="mt-2 text-center text-[11px]" style={{ color: colors.muted }}>Powered by Knowledge Agent</p> : null}
          </div>
        </div>

        <span className="absolute" style={{ [near]: EDGE, bottom: EDGE }}>
          {effects.glow ? (
            <span
              className="absolute -inset-1.5 rounded-full blur-md"
              style={{
                background: `linear-gradient(90deg, ${variables['--ka-glow-1']}, ${variables['--ka-glow-2']}, ${variables['--ka-glow-3']}, ${variables['--ka-glow-4']})`,
                opacity: 0.85,
              }}
            />
          ) : null}
          <span
            data-launcher
            className="relative flex items-center justify-center gap-2 rounded-full px-4 shadow-control"
            style={{
              height: LAUNCHER,
              minWidth: LAUNCHER,
              background: `linear-gradient(135deg, ${colors.launcher}, ${variables['--ka-launcher-end']})`,
              color: colors.launcherIcon,
            }}
          >
            {appearance.launcherAssetId ? (
              <img src={mediaUrl(appearance.launcherAssetId)} alt="" className="size-7 rounded-full object-cover" />
            ) : (
              <Icon className="size-6" />
            )}
            {appearance.launcherLabel ? <span className="max-w-48 truncate text-[15px] font-semibold">{appearance.launcherLabel}</span> : null}
          </span>
        </span>
      </div>
    </div>
  );
}
