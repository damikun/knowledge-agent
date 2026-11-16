import { CATEGORY_TONES } from '../../components/ui/tone';
import type { LabelColor } from './types';

export interface LabelStyle {
  chip: string;
  dot: string;
  name: string;
}

export const LABEL_STYLES: Readonly<Record<LabelColor, LabelStyle>> = Object.fromEntries(
  Object.entries(CATEGORY_TONES).map(([color, tone]) => [color, { chip: `${tone.subtle} ${tone.text}`, dot: tone.solid, name: tone.name }]),
) as Record<LabelColor, LabelStyle>;
