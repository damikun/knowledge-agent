// The launcher's styles, inside its shadow root, so nothing leaks into the host page and the page's CSS stays out.
// Colors come from web/src/widget/shared/theme.ts as --ka-* variables on the host element.
export const LOADER_CSS = `
:host {
  all: initial;
}
.ka-root {
  --ka-gap: 20px;
  --ka-spring: cubic-bezier(0.2, 0.9, 0.3, 1.15);
  --ka-spring: linear(0, 0.18 4.6%, 0.58 11%, 0.96 19.6%, 1.06 25%, 1.08 29%, 1.05 36%, 1.01 46%, 0.995 58%, 1);
  font-family: -apple-system, BlinkMacSystemFont, 'SF Pro Text', system-ui, Inter, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
  -webkit-font-smoothing: antialiased;
}
@keyframes ka-flow {
  to {
    background-position: 300% 0;
  }
}

/* The launcher: a glossy gradient with a soft highlight on top. The gradient sits on ::after so the glow can go behind it. */
.ka-launcher {
  position: fixed;
  z-index: 2147483000;
  right: var(--ka-gap);
  bottom: var(--ka-gap);
  isolation: isolate;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  box-sizing: border-box;
  height: 60px;
  min-width: 60px;
  max-width: calc(100vw - 2 * var(--ka-gap));
  margin: 0;
  padding: 0 22px 0 18px;
  border: 0;
  border-radius: 30px;
  background: none;
  color: var(--ka-launcher-icon);
  font: inherit;
  font-size: 15px;
  font-weight: 600;
  letter-spacing: -0.01em;
  line-height: 1;
  cursor: pointer;
  transition: transform 0.45s var(--ka-spring);
}
.ka-launcher::after {
  content: '';
  position: absolute;
  inset: 0;
  z-index: -1;
  border-radius: inherit;
  background: linear-gradient(135deg, var(--ka-launcher), var(--ka-launcher-end));
  box-shadow:
    inset 0 1px 0 rgb(255 255 255 / 0.32),
    inset 0 -1px 0 rgb(0 0 0 / 0.1),
    0 2px 4px rgb(0 0 0 / 0.12),
    0 10px 24px -6px rgb(0 0 0 / 0.3);
}
/* A soft, slowly moving halo in the glow colors, behind the launcher at rest. */
.ka-glow .ka-launcher::before {
  content: '';
  position: absolute;
  inset: -5px;
  z-index: -2;
  border-radius: inherit;
  background: linear-gradient(90deg, var(--ka-glow-1), var(--ka-glow-2), var(--ka-glow-3), var(--ka-glow-4), var(--ka-glow-1));
  background-size: 300% 100%;
  filter: blur(12px);
  opacity: 0.5;
  animation: ka-flow 9s linear infinite;
}
.ka-round .ka-launcher,
.ka-is-open .ka-launcher {
  width: 60px;
  padding: 0;
}
.ka-left .ka-launcher {
  right: auto;
  left: var(--ka-gap);
}
.ka-launcher:hover {
  transform: translateY(-2px) scale(1.04);
}
.ka-launcher:active {
  transform: scale(0.95);
}
.ka-launcher:focus-visible {
  outline: 2px solid #111111;
  outline-offset: 3px;
  box-shadow: 0 0 0 6px #ffffff;
}
.ka-icon {
  display: inline-flex;
  flex: none;
  width: 26px;
  height: 26px;
}
.ka-icon svg,
.ka-icon img {
  width: 100%;
  height: 100%;
}
.ka-icon img {
  border-radius: 50%;
  object-fit: cover;
}
.ka-label {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.ka-close-icon,
.ka-is-open .ka-open-icon,
.ka-is-open .ka-label {
  display: none;
}
.ka-is-open .ka-close-icon {
  display: inline-flex;
}
.ka-badge {
  position: absolute;
  top: -4px;
  right: -4px;
  box-sizing: border-box;
  min-width: 20px;
  height: 20px;
  padding: 0 6px;
  border-radius: 10px;
  background: #c4320a;
  color: #ffffff;
  font-size: 12px;
  font-weight: 700;
  line-height: 20px;
  text-align: center;
  box-shadow: 0 0 0 2px #ffffff;
}
.ka-badge[hidden] {
  display: none;
}

/*
 * The panel opens with a spring. It only moves and scales: fading it would make it a backdrop root, and the glass
 * would lose the page behind it while it opens.
 */
.ka-panel {
  position: fixed;
  z-index: 2147483000;
  right: var(--ka-gap);
  bottom: calc(var(--ka-gap) + 76px);
  box-sizing: border-box;
  width: 400px;
  height: 660px;
  max-width: calc(100vw - 2 * var(--ka-gap));
  max-height: calc(100vh - var(--ka-gap) - 96px);
  border-radius: var(--ka-radius);
  visibility: hidden;
  transform: translateY(18px) scale(0.92);
  transform-origin: bottom right;
  transition: transform 0.5s var(--ka-spring), visibility 0s linear 0.5s;
}
.ka-left .ka-panel {
  right: auto;
  left: var(--ka-gap);
  transform-origin: bottom left;
}
.ka-is-open .ka-panel {
  visibility: visible;
  transform: none;
  transition: transform 0.5s var(--ka-spring), visibility 0s;
}
/* The surface: tinted glass or a solid panel, with a thin highlight on its edge and layered shadows for depth. */
.ka-surface {
  position: absolute;
  inset: 0;
  overflow: hidden;
  border-radius: inherit;
  background: var(--ka-panel-fill);
  box-shadow:
    inset 0 1px 0 rgb(255 255 255 / 0.7),
    inset 0 0 0 1px rgb(255 255 255 / 0.35),
    0 0 0 0.5px rgb(0 0 0 / 0.1),
    0 2px 6px rgb(0 0 0 / 0.06),
    0 14px 30px -8px rgb(0 0 0 / 0.2),
    0 36px 72px -18px rgb(0 0 0 / 0.32);
}
.ka-dark .ka-surface {
  box-shadow:
    inset 0 1px 0 rgb(255 255 255 / 0.14),
    inset 0 0 0 1px rgb(255 255 255 / 0.08),
    0 0 0 0.5px rgb(0 0 0 / 0.5),
    0 2px 6px rgb(0 0 0 / 0.2),
    0 14px 30px -8px rgb(0 0 0 / 0.4),
    0 36px 72px -18px rgb(0 0 0 / 0.55);
}
.ka-glass .ka-surface {
  -webkit-backdrop-filter: blur(28px) saturate(180%);
  backdrop-filter: blur(28px) saturate(180%);
}
.ka-surface iframe,
.ka-surface ::slotted(*) {
  display: block;
  width: 100%;
  height: 100%;
  border: 0;
  background: transparent;
}
/* While an answer is written: a ring in the glow colors runs around the panel, with a soft glow behind it. */
.ka-glow.ka-busy .ka-panel::before,
.ka-glow.ka-busy .ka-panel::after {
  content: '';
  position: absolute;
  inset: -2px;
  z-index: -1;
  border-radius: calc(var(--ka-radius) + 2px);
  background: linear-gradient(90deg, var(--ka-glow-1), var(--ka-glow-2), var(--ka-glow-3), var(--ka-glow-4), var(--ka-glow-1));
  background-size: 300% 100%;
  animation: ka-flow 3.2s linear infinite;
  pointer-events: none;
}
.ka-glow.ka-busy .ka-panel::before {
  padding: 2px;
  -webkit-mask:
    linear-gradient(#000 0 0) content-box exclude,
    linear-gradient(#000 0 0);
  mask:
    linear-gradient(#000 0 0) content-box exclude,
    linear-gradient(#000 0 0);
}
.ka-glow.ka-busy .ka-panel::after {
  inset: 10px;
  filter: blur(28px);
  opacity: 0.55;
}
@media (max-width: 639px) {
  .ka-panel {
    inset: 0;
    width: 100%;
    height: 100%;
    max-width: none;
    max-height: none;
    border-radius: 0;
    transform: translateY(24px);
  }
  .ka-is-open .ka-launcher {
    display: none;
  }
}
@media (prefers-reduced-motion: reduce) {
  .ka-launcher,
  .ka-panel,
  .ka-is-open .ka-panel {
    transition: none;
  }
  .ka-launcher:hover,
  .ka-launcher:active {
    transform: none;
  }
  .ka-glow .ka-launcher::before,
  .ka-glow.ka-busy .ka-panel::before,
  .ka-glow.ka-busy .ka-panel::after {
    animation: none;
  }
}
@media (prefers-reduced-transparency: reduce) {
  .ka-glass .ka-surface {
    -webkit-backdrop-filter: none;
    backdrop-filter: none;
    background: var(--ka-panel);
  }
}
@media (forced-colors: active) {
  .ka-surface {
    border: 1px solid CanvasText;
  }
  .ka-launcher {
    border: 2px solid ButtonText;
  }
  .ka-glow .ka-launcher::before,
  .ka-glow.ka-busy .ka-panel::before,
  .ka-glow.ka-busy .ka-panel::after {
    display: none;
  }
}
`;
