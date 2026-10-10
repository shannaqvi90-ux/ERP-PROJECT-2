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
 *  - `actions`: what the module lets a user start from anywhere by typing in the command palette
 *    (New user, New company, Print or export the branches list ...). An action opens its screen and,
 *    with `command`, then runs that screen's own command (registered with useCommand/useShortcut),
 *    so the palette starts exactly what the screen's button or key would. The palette offers an
 *    action only to a user whose roles grant its `permission` and the permission of the screen it
 *    opens; the screen's command is the second check (a screen that does not offer the action at
 *    the moment registers no command, and the palette says so instead of doing anything).
 *    Commands of the screen on show are listed by the palette as well, first ("On this screen").
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

/** An action a module offers in the command palette from anywhere (see `actions` above). */
export type ModuleAction = {
  key: string;
  /** String key of the action's name; matched in both languages. */
  labelKey: string;
  /** String key of extra words people type for it ("invite, add, create"), in each language. */
  keywordsKey?: string;
  /** What the user's roles must grant (besides the screen's own permission). */
  permission: string;
  /** The screen the action happens on (a route path). */
  path: string;
  /** Id of the screen command to run once the screen offers it (useCommand/useShortcut). */
  command?: string;
  /**
   * For an action its screen offers only in some cases that permissions do not decide (New branch
   * needs a company the user may add a branch to): asked each time the palette opens, with the
   * screen's own test; the action is offered only once it answers true.
   */
  available?: () => Promise<boolean>;
};

export type ModuleExtensions = {
  topbar?: SlotItem[];
  status?: SlotItem[];
  palette?: PaletteSource[];
  actions?: ModuleAction[];
};

type ExtensionModule = { extensions?: ModuleExtensions };

const found = import.meta.glob<ExtensionModule>("../modules/*/extensions.{ts,tsx}", { eager: true });

export function collectExtensions(sources: Record<string, ExtensionModule>): Required<ModuleExtensions> {
  const all: Required<ModuleExtensions> = { topbar: [], status: [], palette: [], actions: [] };
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
    for (const action of ext.actions ?? []) {
      if (!action.permission) throw new Error(`Action "${action.key}" of module ${name} names no permission`);
      if (!action.path.startsWith(`/${name}/`)) throw new Error(`Action "${action.key}" of module ${name} must happen on one of its own screens (/${name}/...)`);
      all.actions.push(action);
    }
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
