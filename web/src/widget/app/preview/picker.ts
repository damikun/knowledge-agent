import { EDIT_ATTRIBUTE, isEditTarget, type EditTarget, type PreviewMessage, type PreviewRect, type PreviewScene } from '../../shared/preview';

type Root = Document | ShadowRoot;

interface Found {
  target: EditTarget;
  element: Element;
}

function rectOf(element: Element): PreviewRect | null {
  // Nothing to point at with display: none, such as the launcher behind the open panel on a phone.
  if (element.getClientRects().length === 0) {
    return null;
  }
  const { x, y, width, height } = element.getBoundingClientRect();
  return { x, y, width, height };
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

// Reports the editable element under the pointer and the clicked one, and keeps tracking the selected element.
// The roots are the page and the launcher's shadow root, which queries on the page don't reach.
export class PreviewPicker {
  private scene: PreviewScene | null = null;
  // What the editor wants measured: the target, else the first of its fallbacks that shows.
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
      // The launcher's panel moves what is inside it while it opens; its transitions do not leave the shadow root.
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
    // A phone-sized preview hides the launcher behind the open panel, for one.
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

  // After a render the editor asked for: which marked elements show, and where the tracked and hovered ones are now.
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

  // The target before its fallbacks, as soon as it shows again, and the same element while it shows.
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

  // The same element while it shows, else the first one that shows with the same mark: React may have replaced it.
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
    // Without a related target the pointer left the preview.
    if (event.relatedTarget === null) {
      this.setHovered(null);
    }
  };

  // Clicking an editable element selects it; it must not also focus a field or start a text selection.
  private readonly onMouseDown = (event: MouseEvent) => {
    if (targetIn(event)) {
      event.preventDefault();
    }
  };

  private readonly onClick = (event: MouseEvent) => {
    const found = targetIn(event);
    if (found) {
      // The widget's own behavior, such as sending a suggested question, does not run in the editor.
      event.preventDefault();
      event.stopPropagation();
    }
    // Tracked from now on; the editor sends the fallbacks with its next track.
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
