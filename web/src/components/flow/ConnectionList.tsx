export interface Connection {
  id: string;
  from: string;
  to: string;
  label?: string | undefined;
}

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
