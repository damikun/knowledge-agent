import { ArrowRightLeft, BookOpen, Library, Search, Telescope, TextSearch, Undo2, Wrench, type LucideIcon } from 'lucide-react';
import type { JsonValue } from '../../lib/types';
import { ToolNames } from './types';

export interface ToolMeta {
  label: string;
  activeLabel: string;
  icon: LucideIcon;
}

const TOOL_META: Record<string, ToolMeta> = {
  [ToolNames.SearchKnowledgeBase]: {
    label: 'Searched the knowledge base',
    activeLabel: 'Searching the knowledge base',
    icon: Search,
  },
  [ToolNames.GrepSources]: { label: 'Found exact text', activeLabel: 'Finding exact text', icon: TextSearch },
  [ToolNames.FindSources]: { label: 'Looked up sources', activeLabel: 'Looking up sources', icon: Library },
  [ToolNames.ReadSource]: { label: 'Read a source', activeLabel: 'Reading a source', icon: BookOpen },
  [ToolNames.DelegateResearch]: { label: 'Researcher', activeLabel: 'Researcher working', icon: Telescope },
  [ToolNames.TransferToAgent]: { label: 'Handed to a specialist', activeLabel: 'Handing to a specialist', icon: ArrowRightLeft },
  [ToolNames.TransferBack]: { label: 'Handed back', activeLabel: 'Handing back', icon: Undo2 },
};

/** transfer_to_agent and transfer_back: the conversation moves between the front agent and a specialist. */
export function isTransferTool(toolName: string): boolean {
  return toolName === ToolNames.TransferToAgent || toolName === ToolNames.TransferBack;
}

/** "fetch_url" → "Fetch url". */
function humanizeToolName(name: string): string {
  const spaced = name.replace(/[_-]+/g, ' ').replace(/([a-z0-9])([A-Z])/g, '$1 $2').trim();
  return spaced ? spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase() : 'Tool';
}

export function toolMeta(toolName: string): ToolMeta {
  const known = TOOL_META[toolName];
  if (known) {
    return known;
  }
  const label = humanizeToolName(toolName);
  return { label, activeLabel: label, icon: Wrench };
}

export function isDelegateTool(toolName: string): boolean {
  return toolName === ToolNames.DelegateResearch;
}

function asObject(value: JsonValue | null | undefined): Record<string, JsonValue> {
  return value && typeof value === 'object' && !Array.isArray(value) ? value : {};
}

function asText(value: JsonValue | undefined): string | null {
  if (typeof value === 'string') {
    return value.trim() || null;
  }
  if (typeof value === 'number' || typeof value === 'boolean') {
    return String(value);
  }
  return null;
}

export interface ToolArgumentSummary {
  /** The argument that best describes the call: query, task or source. */
  primary: string | null;
  details: string[];
}

/** `sourceTitle` resolves source ids to titles; unknown ids are shown as they are. */
export function summarizeToolArguments(
  toolName: string,
  rawArgs: JsonValue | null | undefined,
  sourceTitle?: (id: string) => string | undefined,
): ToolArgumentSummary {
  const args = asObject(rawArgs);
  const details: string[] = [];
  const sourceIds = Array.isArray(args.source_ids) ? args.source_ids.filter((v): v is string => typeof v === 'string') : [];
  const sourceScope = () => {
    if (sourceIds.length === 1) {
      details.push(sourceTitle?.(sourceIds[0]!) ?? '1 source');
    } else if (sourceIds.length > 1) {
      details.push(`${sourceIds.length} sources`);
    }
  };

  switch (toolName) {
    case ToolNames.SearchKnowledgeBase: {
      const topK = asText(args.top_k);
      if (topK) {
        details.push(`top ${topK}`);
      }
      sourceScope();
      return { primary: asText(args.query), details };
    }
    case ToolNames.GrepSources: {
      const offset = asText(args.offset);
      if (args.output === 'sources') {
        details.push('sources');
      }
      if (args.match_case === true) {
        details.push('match case');
      }
      if (args.whole_words === false) {
        details.push('inside words');
      }
      if (offset && offset !== '0') {
        details.push(`from #${Number(offset) + 1}`);
      }
      sourceScope();
      return { primary: asText(args.text), details };
    }
    case ToolNames.FindSources: {
      const type = asText(args.type);
      const status = asText(args.status);
      const offset = asText(args.offset);
      if (type) {
        details.push(type);
      }
      if (status && status !== 'ready') {
        details.push(status === 'any' ? 'any status' : status);
      }
      if (offset && offset !== '0') {
        details.push(`from #${Number(offset) + 1}`);
      }
      return { primary: asText(args.query) ?? 'all sources', details };
    }
    case ToolNames.ReadSource: {
      const id = asText(args.source_id);
      const offset = asText(args.offset);
      const maxChars = asText(args.max_chars);
      if (offset && offset !== '0') {
        details.push(`from character ${offset}`);
      }
      if (maxChars) {
        details.push(`up to ${maxChars} characters`);
      }
      return { primary: id ? (sourceTitle?.(id) ?? id) : null, details };
    }
    case ToolNames.DelegateResearch: {
      sourceScope();
      return { primary: asText(args.task), details };
    }
    default: {
      const firstText = Object.values(args).map((v) => asText(v)).find((v) => v !== null) ?? null;
      return { primary: firstText, details };
    }
  }
}

/** The source ids a tool card shows by title: read_source's id, or a single scoped source. */
export function toolSourceIds(toolName: string, rawArgs: JsonValue | null | undefined): string[] {
  const ids: string[] = [];
  summarizeToolArguments(toolName, rawArgs, (id) => {
    ids.push(id);
    return undefined;
  });
  return ids;
}
