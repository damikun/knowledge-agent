import { EDIT_ATTRIBUTE, isEditTarget, type CornerRadii, type EditTarget, type PreviewBox, type PreviewMessage, type PreviewRect, type PreviewScene } from '../../shared/preview';

type Root = Document | ShadowRoot;

interface Found {
  target: EditTarget;
  element: Element;
}

const CORNER_TOLERANCE = 1.5;

function boxOf(element: Element): PreviewBox {
  const { x, y, width, height } = element.getBoundingClientRect();
  return { x, y, width, height };
}

function radiiOf(element: Element, box: PreviewBox): CornerRadii {
  const style = getComputedStyle(element);
  const most = Math.min(box.width, box.height) / 2;
  const read = (value: string) => Math.min(Number.parseFloat(value) || 0, most);
  return [read(style.borderTopLeftRadius), read(style.borderTopRightRadius), read(style.borderBottomRightRadius), read(style.borderBottomLeftRadius)];
}

function near(a: number, b: number): boolean {
  return Math.abs(a - b) <= CORNER_TOLERANCE;
}

function panelOf(element: Element): Element | null {
  const root = element.getRootNode();
  const host = root instanceof ShadowRoot ? root.host : element.closest('knowledge-agent-widget');
  const surface = (host?.shadowRoot ?? null)?.querySelector('.ka-surface') ?? null;
  return surface && surface !== element && (surface.contains(element) || host?.contains(element)) ? surface : null;
}

function rectOf(element: Element): PreviewRect | null {
  if (element.getClientRects().length === 0) {
    return null;
  }
  const box = boxOf(element);
  const own = radiiOf(element, box);
  const surface = panelOf(element);
  if (!surface) {
    return { ...box, radii: own };
  }
  const panel = boxOf(surface);
  const panelRadii = radiiOf(surface, panel);
  const left = near(box.x, panel.x);
  const right = near(box.x + box.width, panel.x + panel.width);
  const top = near(box.y, panel.y);
  const bottom = near(box.y + box.height, panel.y + panel.height);
  const radii: CornerRadii = [
    top && left ? Math.max(own[0], panelRadii[0]) : own[0],
    top && right ? Math.max(own[1], panelRadii[1]) : own[1],
    bottom && right ? Math.max(own[2], panelRadii[2]) : own[2],
    bottom && left ? Math.max(own[3], panelRadii[3]) : own[3],
  ];
  return { ...box, radii, panel };
}

function sameRect(a: PreviewRect | null, b: PreviewRect | null): boolean {
  if (a === null || b === null) {
    return a === b;
  }
  return Math.abs(a.x - b.x) < 0.5 && Math.abs(a.y - b.y) < 0.5 && Math.abs(a.width - b.width) < 0.5 && Math.abs(a.height - b.height) < 0.5;
}

function targetIn(event: Event): Found | null {
  for (const node of event.composedPath()) {
    if (node instanceof Element) {
      const target = node.getAttribute(EDIT_ATTRIBUTE);
      if (isEditTarget(target)) {
        return { target, element: node };
      }
    }
  }
  return null;
}

export class PreviewPicker {
  private scene: PreviewScene | null = null;
  private tracked: { target: EditTarget; fallbacks: readonly EditTarget[] } | null = null;
  private anchor: Found | null = null;
  private anchorRect: PreviewRect | null = null;
  private hovered: Found | null = null;
  private hoverRect: PreviewRect | null = null;
  private readonly resizes: ResizeObserver | null;
  private readonly mutations: MutationObserver | null;
  private readonly cleanups: Array<() => void> = [];

  constructor(
    private readonly roots: readonly Root[],
    private readonly post: (message: PreviewMessage) => void,
  ) {
    const refresh = () => this.refresh();
    this.resizes = typeof ResizeObserver === 'function' ? new ResizeObserver(refresh) : null;
    this.mutations = typeof MutationObserver === 'function' ? new MutationObserver(refresh) : null;
    for (const root of roots) {
      this.mutations?.observe(root, { subtree: true, childList: true, characterData: true, attributes: true });
      this.listen(root, 'transitionend', refresh);
    }
    const page = roots[0];
    if (page) {
      this.listen(page, 'pointermove', this.onPointerMove);
      this.listen(page, 'pointerout', this.onPointerOut);
      this.listen(page, 'mousedown', this.onMouseDown, true);
      this.listen(page, 'click', this.onClick, true);
      this.listen(page, 'keydown', this.onKeyDown, true);
      this.listen(page, 'scroll', refresh, true);
    }
    this.listen(window, 'resize', () => {
      if (this.scene) {
        this.rendered(this.scene);
      }
    });
  }

  dispose(): void {
    this.resizes?.disconnect();
    this.mutations?.disconnect();
    for (const cleanup of this.cleanups.splice(0)) {
      cleanup();
    }
  }

  rendered(scene: PreviewScene): void {
    this.scene = scene;
    this.post({ type: 'rendered', scene, targets: this.visibleTargets() });
    this.refresh();
  }

  track(target: EditTarget | null, fallbacks: readonly EditTarget[] = []): void {
    this.tracked = target ? { target, fallbacks } : null;
    this.anchor = this.locate();
    this.anchorRect = this.anchor ? rectOf(this.anchor.element) : null;
    this.observe();
    this.postAnchor();
  }

  private refresh(): void {
    if (this.tracked) {
      const anchor = this.locate();
      const rect = anchor ? rectOf(anchor.element) : null;
      const moved = anchor?.element !== this.anchor?.element;
      if (moved || !sameRect(rect, this.anchorRect)) {
        this.anchor = anchor;
        this.anchorRect = rect;
        if (moved) {
          this.observe();
        }
        this.postAnchor();
      }
    }
    if (this.hovered) {
      const element = this.resolve(this.hovered.target, this.hovered.element);
      const rect = element ? rectOf(element) : null;
      if (!element || !rect) {
        this.setHovered(null);
      } else if (element !== this.hovered.element || !sameRect(rect, this.hoverRect)) {
        this.hovered = { target: this.hovered.target, element };
        this.hoverRect = rect;
        this.observe();
        this.post({ type: 'hover', target: this.hovered.target, rect });
      }
    }
  }

  private postAnchor(): void {
    if (this.tracked) {
      this.post({ type: 'rect', target: this.tracked.target, anchor: this.anchor?.target ?? null, rect: this.anchorRect });
    }
  }

  private locate(): Found | null {
    if (!this.tracked) {
      return null;
    }
    for (const target of [this.tracked.target, ...this.tracked.fallbacks]) {
      const element = this.anchor?.target === target ? this.resolve(target, this.anchor.element) : (this.find(target)?.element ?? null);
      if (element) {
        return { target, element };
      }
    }
    return null;
  }

  private resolve(target: EditTarget, element: Element | null): Element | null {
    if (element?.isConnected && element.getAttribute(EDIT_ATTRIBUTE) === target && rectOf(element)) {
      return element;
    }
    return this.find(target)?.element ?? null;
  }

  private find(target: EditTarget): Found | null {
    for (const root of this.roots) {
      for (const element of root.querySelectorAll(`[${EDIT_ATTRIBUTE}="${target}"]`)) {
        if (rectOf(element)) {
          return { target, element };
        }
      }
    }
    return null;
  }

  private visibleTargets(): EditTarget[] {
    const shown = new Set<EditTarget>();
    for (const root of this.roots) {
      for (const element of root.querySelectorAll(`[${EDIT_ATTRIBUTE}]`)) {
        const target = element.getAttribute(EDIT_ATTRIBUTE);
        if (isEditTarget(target) && rectOf(element)) {
          shown.add(target);
        }
      }
    }
    return [...shown];
  }

  private setHovered(found: Found | null): void {
    if (found?.element === this.hovered?.element) {
      return;
    }
    this.hovered = found;
    this.hoverRect = found ? rectOf(found.element) : null;
    this.observe();
    this.post({ type: 'hover', target: found?.target ?? null, rect: this.hoverRect });
  }

  private observe(): void {
    if (!this.resizes) {
      return;
    }
    this.resizes.disconnect();
    for (const element of [this.anchor?.element, this.hovered?.element]) {
      if (element?.isConnected) {
        this.resizes.observe(element);
      }
    }
  }

  private listen<E extends Event>(target: EventTarget, type: string, listener: (event: E) => void, capture = false): void {
    const handler = listener as EventListener;
    target.addEventListener(type, handler, { capture, passive: type === 'scroll' || type === 'pointermove' });
    this.cleanups.push(() => target.removeEventListener(type, handler, { capture }));
  }

  private readonly onPointerMove = (event: PointerEvent) => {
    this.setHovered(targetIn(event));
  };

  private readonly onPointerOut = (event: PointerEvent) => {
    if (event.relatedTarget === null) {
      this.setHovered(null);
    }
  };

  private readonly onMouseDown = (event: MouseEvent) => {
    if (targetIn(event)) {
      event.preventDefault();
    }
  };

  private readonly onClick = (event: MouseEvent) => {
    const found = targetIn(event);
    if (found) {
      event.preventDefault();
      event.stopPropagation();
    }
    this.tracked = found ? { target: found.target, fallbacks: [] } : null;
    this.anchor = found;
    this.anchorRect = found ? rectOf(found.element) : null;
    this.observe();
    this.post({ type: 'select', target: found?.target ?? null, rect: this.anchorRect });
  };

  private readonly onKeyDown = (event: KeyboardEvent) => {
    if (event.key === 'Escape' && this.tracked) {
      this.track(null);
      this.post({ type: 'select', target: null, rect: null });
    }
  };
}
