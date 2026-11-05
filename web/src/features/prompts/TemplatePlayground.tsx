import { RotateCcw } from 'lucide-react';
import { useState } from 'react';
import { Button } from '../../components/ui/Button';
import { Card, ErrorMessage, Spinner } from '../../components/ui/feedback';
import { useDebouncedValue } from '../../lib/hooks/useDebouncedValue';
import { useRenderedTemplate } from './api';
import { HandlebarsCheatSheet } from './HandlebarsCheatSheet';
import { formatJson, parseVariables } from './json';
import { JsonEditor } from './JsonEditor';
import { RenderedOutput } from './RenderedOutput';
import { PLAYGROUND_TEMPLATE, PLAYGROUND_VARIABLES } from './samples';

const INITIAL_VARIABLES = formatJson(PLAYGROUND_VARIABLES);

export function TemplatePlayground() {
  const [template, setTemplate] = useState(PLAYGROUND_TEMPLATE);
  const [variables, setVariables] = useState(INITIAL_VARIABLES);
  const parsed = parseVariables(variables);
  const settledTemplate = useDebouncedValue(template);
  const settledVariables = useDebouncedValue(variables);
  const settledParsed = parseVariables(settledVariables);
  const render = useRenderedTemplate(settledTemplate, settledVariables, settledParsed.ok ? settledParsed.value : null);

  return (
    <div className="@container">
      <div className="grid items-start gap-5 @3xl:grid-cols-2">
        <Card className="space-y-4 p-5">
          <div className="flex flex-wrap items-start justify-between gap-x-4 gap-y-2">
            <div className="min-w-0 flex-1 basis-48">
              <h2 className="text-section text-fg">Template</h2>
              <p className="mt-0.5 text-small text-fg-secondary">Try Handlebars on sample variables. Nothing is saved.</p>
            </div>
            <Button
              variant="ghost"
              size="sm"
              icon={RotateCcw}
              disabled={template === PLAYGROUND_TEMPLATE && variables === INITIAL_VARIABLES}
              onClick={() => {
                setTemplate(PLAYGROUND_TEMPLATE);
                setVariables(INITIAL_VARIABLES);
              }}
            >
              Reset example
            </Button>
          </div>
          <div className="space-y-1.5">
            <label htmlFor="playground-template" className="block text-small font-medium text-fg">
              Template
            </label>
            <textarea
              id="playground-template"
              value={template}
              rows={14}
              spellCheck={false}
              onChange={(event) => setTemplate(event.target.value)}
              className="field resize-y font-mono text-xs leading-relaxed"
            />
          </div>
          <JsonEditor id="playground-variables" label="Sample variables" value={variables} onChange={setVariables} error={parsed.ok ? null : parsed.error} rows={10} />
        </Card>

        <div className="min-w-0 space-y-5">
          <Card className="space-y-3 p-5">
            <div className="flex items-center justify-between gap-2">
              <h2 className="text-section text-fg">Live output</h2>
              {render.isFetching ? <Spinner label="Rendering" className="size-3.5" /> : null}
            </div>
            {render.isError ? <ErrorMessage error={render.error} title="Rendering failed" /> : null}
            {!template.trim() ? <p className="text-small text-fg-muted">Write a template to see its output.</p> : null}
            {render.data && template.trim() ? (
              <>
                <RenderedOutput text={render.data.output} />
                {render.data.variables.length > 0 ? (
                  <div className="flex flex-wrap items-center gap-1.5 text-xs text-fg-muted">
                    <span>Variables used</span>
                    {render.data.variables.map((variable) => (
                      <code key={variable} className="rounded-sm bg-accent-subtle px-1.5 py-0.5 font-mono text-accent-text">
                        {variable}
                      </code>
                    ))}
                  </div>
                ) : null}
              </>
            ) : null}
          </Card>
          <HandlebarsCheatSheet />
        </div>
      </div>
    </div>
  );
}
