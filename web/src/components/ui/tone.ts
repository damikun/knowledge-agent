import { CircleAlert, CircleCheck, Info, TriangleAlert, type LucideIcon } from 'lucide-react';

export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

export interface ToneStyle {
  solid: string;
  subtle: string;
  text: string;
  icon: string;
  Icon: LucideIcon;
}

export const TONES: Readonly<Record<Tone, ToneStyle>> = {
  neutral: { solid: 'bg-fg-muted', subtle: 'bg-fill-muted', text: 'text-fg-secondary', icon: 'text-fg-muted', Icon: Info },
  info: { solid: 'bg-info', subtle: 'bg-info-subtle', text: 'text-info-text', icon: 'text-info', Icon: Info },
  success: { solid: 'bg-success', subtle: 'bg-success-subtle', text: 'text-success-text', icon: 'text-success', Icon: CircleCheck },
  warning: { solid: 'bg-warning', subtle: 'bg-warning-subtle', text: 'text-warning-text', icon: 'text-warning', Icon: TriangleAlert },
  danger: { solid: 'bg-danger', subtle: 'bg-danger-subtle', text: 'text-danger-text', icon: 'text-danger', Icon: CircleAlert },
};

export type CategoryColor = 'gray' | 'blue' | 'green' | 'amber' | 'red' | 'purple' | 'teal' | 'pink';

export interface CategoryStyle {
  solid: string;
  subtle: string;
  text: string;
  /** Shown in the color picker; the API names stay as they are. */
  name: string;
}

// Muted tag-* tokens, apart from the status tones so a label never reads as an alarm.
// "pink" shows as clay and "purple" as heather.
export const CATEGORY_TONES: Readonly<Record<CategoryColor, CategoryStyle>> = {
  gray: { solid: 'bg-tag-gray', subtle: 'bg-tag-gray-subtle', text: 'text-tag-gray-text', name: 'Gray' },
  blue: { solid: 'bg-tag-blue', subtle: 'bg-tag-blue-subtle', text: 'text-tag-blue-text', name: 'Blue' },
  green: { solid: 'bg-tag-green', subtle: 'bg-tag-green-subtle', text: 'text-tag-green-text', name: 'Green' },
  amber: { solid: 'bg-tag-amber', subtle: 'bg-tag-amber-subtle', text: 'text-tag-amber-text', name: 'Amber' },
  red: { solid: 'bg-tag-red', subtle: 'bg-tag-red-subtle', text: 'text-tag-red-text', name: 'Red' },
  purple: { solid: 'bg-tag-purple', subtle: 'bg-tag-purple-subtle', text: 'text-tag-purple-text', name: 'Heather' },
  teal: { solid: 'bg-tag-teal', subtle: 'bg-tag-teal-subtle', text: 'text-tag-teal-text', name: 'Spruce' },
  pink: { solid: 'bg-tag-pink', subtle: 'bg-tag-pink-subtle', text: 'text-tag-pink-text', name: 'Clay' },
};
