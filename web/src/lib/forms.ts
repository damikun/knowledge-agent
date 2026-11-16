export function parsePositiveInt(value: string): number | null | 'invalid' {
  const trimmed = value.trim();
  if (!trimmed) {
    return null;
  }
  if (!/^\d+$/.test(trimmed)) {
    return 'invalid';
  }
  const parsed = Number.parseInt(trimmed, 10);
  return parsed > 0 ? parsed : 'invalid';
}
