import type { ComponentType } from "react";
import type { Language } from "./format";

/**
 * Extension points of the app shell. A module contributes to the shell from its own folder by
 * exporting `extensions` from `src/modules/<module>/extensions.ts(x)`; nothing central is edited.
 *
 *  - `topbar`: controls in the top bar's context area, right after the workspace name (inline
 *    start side). This is where the active company/branch switcher goes (p02). Each item renders
 *    with no props, reads what it needs through useSession()/useI18n(), and may register its own
 *    shortcut with useShortcut() (it then appears in the shortcut help sheet).
 *  - `status`: short facts in the status line (inline end side).
 *  - `palette`: sources of command palette entries: records found by what the user types
 *    (`minLength` >= 1) or a fixed set of actions (`minLength` 0, for example "switch to company X").
 *
 * Every item may name a `permission`; the shell shows it only to users whose roles grant it.
 * The API enforces permissions anyway; hiding is so screens never offer what the user cannot do.
 */
export type SlotItem = {
  key: string;
  /** Lower comes first. */
  order?: number;
  permission?: string;
  component: ComponentType;
};

export type PaletteItem = {
  id: string;
  /** Already translated or data text (a record's name). */
  title: string;
  subtitle?: string;
  /** Opens this address; or `run` for an action. */
  path?: string;
  run?: () => void | Promise<void>;
  /** Text written left to right whatever the screen direction (e-mails, codes). */
  ltr?: boolean;
};

export type PaletteSourceContext = { language: Language; signal: AbortSignal };

/** A source's answer: the records to show and, when the source knows it, how many match in all. */
export type PaletteResult = { items: PaletteItem[]; total?: number };

export type PaletteSource = {
  key: string;
  /** String key of the heading the results appear under. */
  labelKey: string;
  permission?: string;
  /** Characters typed before the source is asked; 0 means it also answers an empty query. */
  minLength: number;
  search: (query: string, context: PaletteSourceContext) => Promise<PaletteItem[] | PaletteResult>;
  /**
   * The address of a screen that lists every match of the query. When the source matched more
   * than it shows (or does not say how many matched), the palette ends its results with a "Show
   * all matches" entry that opens it.
   */
  showAll?: (query: string) => string;
};

export type ModuleExtensions = {
  topbar?: SlotItem[];
  status?: SlotItem[];
  palette?: PaletteSource[];
};

type ExtensionModule = { extensions?: ModuleExtensions };

const found = import.meta.glob<ExtensionModule>("../modules/*/extensions.{ts,tsx}", { eager: true });

export function collectExtensions(sources: Record<string, ExtensionModule>): Required<ModuleExtensions> {
  const all: Required<ModuleExtensions> = { topbar: [], status: [], palette: [] };
  const seen = new Set<string>();
  for (const [path, module] of Object.entries(sources).sort(([a], [b]) => a.localeCompare(b))) {
    const name = /modules\/([^/]+)\//.exec(path)?.[1] ?? path;
    const ext = module.extensions;
    if (!ext) continue;
    for (const [slot, items] of Object.entries(ext) as [keyof ModuleExtensions, { key: string }[] | undefined][]) {
      for (const item of items ?? []) {
        if (!item.key.startsWith(`${name}.`)) throw new Error(`Extension "${item.key}" of module ${name} must start with "${name}."`);
        const id = `${slot}:${item.key}`;
        if (seen.has(id)) throw new Error(`Extension "${item.key}" is registered twice in ${slot}`);
        seen.add(id);
      }
    }
    all.topbar.push(...(ext.topbar ?? []));
    all.status.push(...(ext.status ?? []));
    all.palette.push(...(ext.palette ?? []));
  }
  const byOrder = (a: SlotItem, b: SlotItem) => (a.order ?? 100) - (b.order ?? 100) || a.key.localeCompare(b.key);
  all.topbar.sort(byOrder);
  all.status.sort(byOrder);
  return all;
}

export const extensions = collectExtensions(found);

/** The items a user may see. */
export function allowed<T extends { permission?: string }>(items: T[], can: (permission: string) => boolean): T[] {
  return items.filter((item) => !item.permission || can(item.permission));
}
