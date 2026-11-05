import { CATEGORY_TONES, type CategoryStyle } from '../../components/ui/tone';
import type { LabelColor } from './types';

export function labelStyle(color: LabelColor): CategoryStyle {
  return CATEGORY_TONES[color] ?? CATEGORY_TONES.gray;
}
