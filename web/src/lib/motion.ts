/** Whether the visitor asked for less motion; scripted scrolling and zooming skip their animation then. */
export function prefersReducedMotion(): boolean {
  return typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/** For scrollIntoView and scrollTo: CSS scroll-behavior does not apply to an explicit 'smooth'. */
export function scrollBehavior(): ScrollBehavior {
  return prefersReducedMotion() ? 'auto' : 'smooth';
}
