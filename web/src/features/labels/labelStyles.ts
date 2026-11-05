import { CATEGORY_TONES } from '../../components/ui/tone';
import type { LabelColor } from './types';

export interface LabelStyle {
  /** From the category tokens, so text stays readable in both themes. */
  chip: string;
  dot: string;
  /** For the color picker and screen readers. */
  name: string;
}

// Each API color maps to its own muted category tone (components/ui/tone.ts), so all eight stay distinct.
export const LABEL_STYLES: Readonly<Record<LabelColor, LabelStyle>> = Object.fromEntries(
  Object.entries(CATEGORY_TONES).map(([color, tone]) => [color, { chip: `${tone.subtle} ${tone.text}`, dot: tone.solid, name: tone.name }]),
) as Record<LabelColor, LabelStyle>;
