import type { ComponentType } from "react";
import { useSession } from "./session";

/**
 * Small extension points of the app shell. A module contributes items from its own
 * `src/modules/<module>/shell.tsx` (exporting `topBarItems`); the shell renders each slot where
 * it belongs, so a module adds to the frame without editing shell files.
 */
export type ShellItem = {
  /** Unique key, starting with the module name. */
  key: string;
  /** Lower comes first. */
  order: number;
  /** Shown only to users holding this permission. */
  permission?: string;
  component: ComponentType;
};

type ShellModule = { topBarItems?: ShellItem[] };

const shellModules = import.meta.glob<ShellModule>("../modules/*/shell.tsx", { eager: true });

export function collectItems(modules: Record<string, ShellModule>): ShellItem[] {
  return Object.values(modules)
    .flatMap((m) => m.topBarItems ?? [])
    .sort((a, b) => a.order - b.order || a.key.localeCompare(b.key));
}

export const topBarItems: ShellItem[] = collectItems(shellModules);

/** Items modules add to the top bar (for example the working company switcher). */
export function TopBarSlot({ items = topBarItems }: { items?: ShellItem[] }) {
  const { can } = useSession();
  return (
    <>
      {items
        .filter((item) => !item.permission || can(item.permission))
        .map((item) => (
          <item.component key={item.key} />
        ))}
    </>
  );
}
