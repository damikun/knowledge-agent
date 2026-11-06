/**
 * A visually hidden live region that stays mounted, so a screen reader hears each new message (WCAG 4.1.3).
 * Give it an empty message until there is something new: text present on mount is not announced.
 */
export function Announcer({ message }: { message: string }) {
  return (
    <p role="status" className="sr-only">
      {message}
    </p>
  );
}
