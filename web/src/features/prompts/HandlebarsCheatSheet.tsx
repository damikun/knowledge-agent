import { Card } from '../../components/ui/feedback';

const ENTRIES: Array<{ syntax: string; description: string }> = [
  { syntax: '{{name}} · {{user.name}}', description: 'Insert a value (no HTML escaping)' },
  { syntax: '{{#if x}}…{{else}}…{{/if}}', description: 'Conditional block' },
  { syntax: '{{#unless x}}…{{/unless}}', description: 'Inverse conditional' },
  { syntax: '{{#each items}}{{@index}}. {{title}}{{/each}}', description: 'Loop; @index, @first, @last, this' },
  { syntax: '{{#with user}}{{name}}{{/with}}', description: 'Change the context' },
  { syntax: '{{#if (eq tone "formal")}}…{{/if}}', description: 'Comparisons: eq ne gt gte lt lte' },
  { syntax: '{{#if (and a (or b (not c)))}}', description: 'Logic: and or not' },
  { syntax: '{{default language "English"}}', description: 'Fallback for empty values' },
  { syntax: '{{json config}}', description: 'Serialize a value as JSON' },
  { syntax: '{{upper code}} · {{lower name}}', description: 'Change case' },
  { syntax: '{{truncate text 120}}', description: 'Shorten to n characters' },
  { syntax: '{{join tags ", "}}', description: 'Join a list' },
  { syntax: '{{date today "yyyy-MM-dd"}}', description: 'Format a date' },
  { syntax: '{{!-- note --}}', description: 'Comment (not rendered)' },
];

export function HandlebarsCheatSheet() {
  return (
    <Card>
      <div className="px-5 pt-4 pb-2">
        <h2 className="text-section text-fg">Handlebars cheat sheet</h2>
        <p className="mt-0.5 text-small text-fg-secondary">Rendering happens on the API with Handlebars.Net, exactly as the agent sees it.</p>
      </div>
      <dl className="divide-y divide-border-subtle text-xs">
        {ENTRIES.map((entry) => (
          <div key={entry.syntax} className="grid gap-0.5 px-5 py-2 sm:grid-cols-[minmax(0,1.3fr)_minmax(0,1fr)] sm:gap-3">
            <dt>
              <code className="font-mono break-words text-fg">{entry.syntax}</code>
            </dt>
            <dd className="text-fg-secondary">{entry.description}</dd>
          </div>
        ))}
      </dl>
    </Card>
  );
}
