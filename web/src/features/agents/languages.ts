/** Stored by English name ("German"), which the prompt uses; empty answers in each question's language. */
const LANGUAGES: ReadonlyArray<{ value: string; label: string }> = [
  { value: 'English', label: 'English' },
  { value: 'German', label: 'Deutsch' },
  { value: 'Spanish', label: 'Español' },
  { value: 'French', label: 'Français' },
  { value: 'Italian', label: 'Italiano' },
  { value: 'Dutch', label: 'Nederlands' },
  { value: 'Polish', label: 'Polski' },
  { value: 'Portuguese', label: 'Português' },
  { value: 'Czech', label: 'Čeština' },
  { value: 'Slovak', label: 'Slovenčina' },
  { value: 'Hungarian', label: 'Magyar' },
  { value: 'Swedish', label: 'Svenska' },
  { value: 'Danish', label: 'Dansk' },
  { value: 'Norwegian', label: 'Norsk' },
  { value: 'Finnish', label: 'Suomi' },
  { value: 'Romanian', label: 'Română' },
  { value: 'Ukrainian', label: 'Українська' },
  { value: 'Russian', label: 'Русский' },
  { value: 'Turkish', label: 'Türkçe' },
  { value: 'Greek', label: 'Ελληνικά' },
  { value: 'Arabic', label: 'العربية' },
  { value: 'Hebrew', label: 'עברית' },
  { value: 'Hindi', label: 'हिन्दी' },
  { value: 'Japanese', label: '日本語' },
  { value: 'Korean', label: '한국어' },
  { value: 'Chinese', label: '中文' },
];

const SAME_AS_VISITOR = { value: '', label: 'Same as the visitor', hint: 'Answers in the language of each question' } as const;

export function languageOptions(current: string): Array<{ value: string; label: string; hint?: string }> {
  const options = [
    { ...SAME_AS_VISITOR },
    ...LANGUAGES.map((language) => ({ value: language.value, label: language.label, ...(language.label === language.value ? {} : { hint: language.value }) })),
  ];
  const own = current.trim();
  return own && !matchLanguage(own) ? [...options, { value: own, label: own, hint: 'Your own' }] : options;
}

/** The list's value for a saved language, ignoring case, such as "german" → "German". */
export function matchLanguage(value: string): string | undefined {
  const wanted = value.trim().toLowerCase();
  return LANGUAGES.find((language) => language.value.toLowerCase() === wanted || language.label.toLowerCase() === wanted)?.value;
}

/** "Deutsch (German)" for a saved "German"; other values as they are. */
export function languageName(value: string): string {
  const match = LANGUAGES.find((language) => language.value === matchLanguage(value));
  return match ? (match.label === match.value ? match.label : `${match.label} (${match.value})`) : value.trim();
}
