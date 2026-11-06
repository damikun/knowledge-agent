export interface Connection {
  id: string;
  from: string;
  to: string;
  label?: string | undefined;
}

/** A diagram's arrows in words: its nodes say what each part is, this list says how they connect (WCAG 1.1.1). */
export function ConnectionList({ label, connections }: { label: string; connections: readonly Connection[] }) {
  if (connections.length === 0) {
    return null;
  }
  return (
    <ul className="sr-only" aria-label={label}>
      {connections.map((connection) => (
        <li key={connection.id}>
          {connection.from} to {connection.to}
          {connection.label ? ` (${connection.label})` : ''}
        </li>
      ))}
    </ul>
  );
}
