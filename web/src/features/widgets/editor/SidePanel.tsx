import { clsx } from 'clsx';
import { ChevronDown, ChevronRight, CircleAlert, Search, X } from 'lucide-react';
import { useId, useState, type ReactNode, type Ref } from 'react';
import { flushSync } from 'react-dom';
import { TabPanel, Tabs } from '../../../components/ui/Tabs';
import type { EditTarget, PreviewScene } from '../../../widget/shared/preview';
import { HandoverFlow } from '../handover/HandoverFlow';
import { handoverDraftFromForm } from '../handover/handoverSteps';
import { AgentAndWidget, AnsweredBy } from './AnsweredBy';
import { PropertyField, type EditorKit } from './controls';
import { inMode, samePath, type DraftPath } from './draft';
import { ElementView } from './ElementView';
import { EmbedSnippet } from './EmbedSnippet';
import type { IdentityState } from './IdentityVerification';
import { Outline } from './OutlineTab';
import type { PanelSection, Registry, SectionId } from './registry';
import { SearchResults } from './SettingsSearch';
import { ThemeTab } from './ThemeTab';

export type PanelTab = 'elements' | 'theme' | 'settings';
export type PanelId = SectionId | 'embed';

const TABS = [
  { id: 'elements', label: 'Elements' },
  { id: 'theme', label: 'Theme' },
  { id: 'settings', label: 'Settings' },
] as const;

function Disclosure({
  id,
  title,
  open,
  onToggle,
  errors = 0,
  edited = 0,
  children,
}: {
  id: PanelId;
  title: string;
  open: boolean;
  onToggle: (id: PanelId) => void;
  errors?: number;
  edited?: number;
  children: ReactNode;
}) {
  const panelId = useId();
  return (
    <section className="border-b border-border-subtle last:border-b-0">
      <h2>
        <button
          type="button"
          aria-expanded={open}
          aria-controls={panelId}
          onClick={() => onToggle(id)}
          className="flex h-10 w-full items-center gap-2 px-4 text-left text-sm font-semibold text-fg hover:bg-fill-hover focus-visible:outline-offset-[-2px]"
        >
          {open ? <ChevronDown className="size-4 text-fg-muted" aria-hidden /> : <ChevronRight className="size-4 text-fg-muted" aria-hidden />}
          <span className="flex-1">{title}</span>
          {edited > 0 ? (
            <span className="inline-flex items-center gap-1 text-xs font-medium text-warning-text">
              <span aria-hidden className="size-1.5 rounded-full bg-warning" />
              {edited} edited
            </span>
          ) : null}
          {errors > 0 ? (
            <span className="inline-flex items-center gap-1 text-xs font-medium text-danger-text">
              <CircleAlert className="size-3.5" aria-hidden />
              {errors === 1 ? '1 to fix' : `${errors} to fix`}
            </span>
          ) : null}
        </button>
      </h2>
      {open ? (
        <div id={panelId} className="space-y-4 px-4 pt-1 pb-5">
          {children}
        </div>
      ) : null}
    </section>
  );
}

export interface SidePanelProps {
  registry: Registry;
  kit: EditorKit;
  scene: PreviewScene;
  tab: PanelTab;
  onTabChange: (tab: PanelTab) => void;
  open: ReadonlySet<PanelId>;
  onToggle: (id: PanelId) => void;
  selected: EditTarget | null;
  shown: ReadonlySet<EditTarget> | null;
  onSelect: (target: EditTarget) => void;
  errorCount: (properties: readonly string[]) => number;
  textErrors: string[] | undefined;
  onResetColors: () => void;
  publicKey: string;
  enabled: boolean;
  identity: IdentityState;
  variant?: 'side' | 'sheet';
  headerRef?: Ref<HTMLDivElement>;
  contentInert?: boolean;
  onExpand?: () => void;
  notShown: boolean;
  focusId: string | null;
  revealCount: number;
  onBack: () => void;
  onOpenTheme: () => void;
  onShowLauncher: () => void;
}

export function SidePanel({
  registry,
  kit,
  scene,
  tab,
  onTabChange,
  open,
  onToggle,
  selected,
  shown,
  onSelect,
  errorCount,
  textErrors,
  onResetColors,
  publicKey,
  enabled,
  identity,
  variant = 'side',
  headerRef,
  contentInert = false,
  onExpand,
  notShown,
  focusId,
  revealCount,
  onBack,
  onOpenTheme,
  onShowLauncher,
}: SidePanelProps) {
  const prefix = useId();
  const searchId = useId();
  const [query, setQuery] = useState('');
  const element = selected ? registry.target(selected) : null;
  const itemId = (target: EditTarget) => `${prefix}-element-${target}`;
  const editedCount = (properties: readonly DraftPath[]) =>
    properties.filter((id) => inMode(id, kit.mode) && !samePath(kit.values, kit.saved, id)).length;
  const sections = new Map(registry.sections.map((section) => [section.id, section]));
  const section = (id: Exclude<SectionId, 'theme'>, extra?: ReactNode, after?: ReactNode) => {
    const definition: PanelSection | undefined = sections.get(id);
    if (!definition || definition.properties.length === 0) {
      return null;
    }
    const errors = errorCount(definition.properties) + (id === 'texts' && textErrors ? 1 : 0);
    return (
      <Disclosure
        id={id}
        title={definition.label}
        open={open.has(id)}
        onToggle={onToggle}
        errors={errors}
        edited={editedCount(definition.properties)}
      >
        {extra}
        {definition.properties.map((property) => {
          const def = registry.properties.get(property);
          return def ? <PropertyField key={property} property={def} kit={kit} /> : null;
        })}
        {after}
      </Disclosure>
    );
  };

  return (
    <div
      {...(variant === 'side' ? { role: 'region', 'aria-label': 'Settings' } : {})}
      className={clsx('flex h-full min-h-0 flex-col overflow-hidden', variant === 'side' && 'rounded-2xl border border-border bg-surface')}
    >
      {variant === 'side' ? <AnsweredBy agents={kit.agents} agentId={kit.values.agentId} className="shrink-0" /> : null}
      {element ? (
        <ElementView
          key={`${element.id}-${revealCount}`}
          element={element}
          registry={registry}
          kit={kit}
          scene={scene}
          notShown={notShown}
          focusId={focusId}
          onBack={() => {
            // Render the outline synchronously so the entry just left can take focus back.
            flushSync(onBack);
            document.getElementById(itemId(element.id))?.focus();
          }}
          onOpenTheme={onOpenTheme}
          onShowLauncher={onShowLauncher}
        />
      ) : (
        <>
          <div ref={headerRef} className={clsx('space-y-2.5 border-b border-border-subtle', variant === 'side' ? 'p-3' : 'px-3 pb-3')}>
            <div className="relative">
              <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-fg-muted" aria-hidden />
              <label htmlFor={searchId} className="sr-only">
                Find a setting
              </label>
              <input
                id={searchId}
                type="search"
                value={query}
                onChange={(event) => {
                  setQuery(event.target.value);
                  onExpand?.();
                }}
                onKeyDown={(event) => {
                  if (event.key === 'Escape' && query) {
                    event.stopPropagation();
                    setQuery('');
                  }
                }}
                placeholder="Find a setting"
                className="field h-8 py-0 pr-8 pl-8 text-small [&::-webkit-search-cancel-button]:appearance-none"
              />
              {query ? (
                <button
                  type="button"
                  onClick={() => setQuery('')}
                  aria-label="Clear the search"
                  className="absolute top-1/2 right-1 grid size-6 -translate-y-1/2 place-items-center rounded-md text-fg-muted hover:bg-fill-hover hover:text-fg"
                >
                  <X className="size-3.5" aria-hidden />
                </button>
              ) : null}
            </div>
            {query ? null : (
              <Tabs
                idPrefix={prefix}
                items={TABS}
                value={tab}
                onChange={(next) => {
                  onTabChange(next);
                  onExpand?.();
                }}
                label="Settings"
                className="w-full [&>button]:flex-1 [&>button]:justify-center"
              />
            )}
          </div>
          <div inert={contentInert} className="min-h-0 flex-1 overflow-y-auto">
            {variant === 'sheet' && !query ? <AnsweredBy agents={kit.agents} agentId={kit.values.agentId} /> : null}
            {query ? (
              <SearchResults registry={registry} kit={kit} query={query} />
            ) : tab === 'elements' ? (
              <TabPanel idPrefix={prefix} id="elements" className="p-4">
                <Outline
                  registry={registry}
                  scene={scene}
                  shown={shown}
                  invalid={(target) => errorCount(target.properties) > 0}
                  edited={(target) => editedCount(target.properties) > 0}
                  onSelect={onSelect}
                  itemId={itemId}
                />
              </TabPanel>
            ) : tab === 'theme' ? (
              <TabPanel idPrefix={prefix} id="theme">
                <ThemeTab registry={registry} kit={kit} onResetColors={onResetColors} />
              </TabPanel>
            ) : (
              <TabPanel idPrefix={prefix} id="settings">
                {section('general', undefined, <AgentAndWidget agents={kit.agents} values={kit.values} />)}
                {section('security')}
                {section(
                  'handover',
                  undefined,
                  kit.values.handoverEnabled ? (
                    <HandoverFlow draft={handoverDraftFromForm(kit.values)} agents={kit.agents} integrations={kit.integrations} />
                  ) : null,
                )}
                {section('conversations')}
                {section(
                  'texts',
                  <>
                    <p className="text-xs text-fg-muted">
                      Texts that show only in some moments, or only to screen readers. Empty fields keep the default.
                    </p>
                    {textErrors ? <p className="text-xs text-danger-text">{textErrors.join(' ')}</p> : null}
                  </>,
                )}
                <Disclosure id="embed" title="Embed code" open={open.has('embed')} onToggle={onToggle}>
                  <p className="text-xs text-fg-muted">
                    Paste it before the closing body tag of every page that should show the chat.
                    {enabled ? '' : ' The widget is disabled, so it does not show yet.'}
                  </p>
                  <EmbedSnippet publicKey={publicKey} identity={identity} />
                </Disclosure>
              </TabPanel>
            )}
          </div>
        </>
      )}
    </div>
  );
}
