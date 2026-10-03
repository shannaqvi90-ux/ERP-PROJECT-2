import { hasString } from "../../kernel/i18n";
import type { MenuItem } from "../../kernel/session";

export type NavGroup = { key: string | null; labelKey: string | null; items: MenuItem[] };

/** String key of a menu group's heading ("settings" -> shell.group.settings). */
export const groupLabelKey = (group: string): string => `shell.group.${group}`;

/**
 * Menu entries (already filtered by the user's permissions and ordered by the server) gathered
 * into groups. Groups keep the order of their first entry; entries without a group come first.
 */
export function groupMenu(menu: MenuItem[]): NavGroup[] {
  const groups: NavGroup[] = [];
  const byKey = new Map<string | null, NavGroup>();
  for (const item of menu) {
    const key = item.group ?? null;
    let group = byKey.get(key);
    if (!group) {
      group = { key, labelKey: key && hasString(groupLabelKey(key)) ? groupLabelKey(key) : null, items: [] };
      byKey.set(key, group);
      if (key === null) groups.unshift(group);
      else groups.push(group);
    }
    group.items.push(item);
  }
  return groups;
}

/** The menu entry a path belongs to (exact, else the longest entry path that prefixes it). */
export function entryFor(menu: MenuItem[], path: string): MenuItem | undefined {
  const exact = menu.find((m) => m.path === path);
  if (exact) return exact;
  return menu
    .filter((m) => path.startsWith(m.path.endsWith("/") ? m.path : `${m.path}/`))
    .sort((a, b) => b.path.length - a.path.length)[0];
}
