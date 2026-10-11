import type { ModuleAction } from "../../kernel/extensions";
import { translate, type Language } from "../../kernel/i18n";
import { matchRoute, routes as allRoutes, type RouteDef } from "../../kernel/router";
import type { CommandDef } from "../../kernel/shortcuts";
import type { PaletteEntry } from "./CommandPalette";

type Translate = (key: string) => string;

const other = (language: Language): Language => (language === "ar" ? "en" : "ar");

/** The words of a keywords string ("invite, add, create" / "دعوة، إضافة"). */
export const keywordsOf = (text: string): string[] => text.split(/[,،;؛]/).map((w) => w.trim()).filter(Boolean);

/** What a text key says in both languages, for matching: the label in the other language and the
 * extra words in both. */
function matchTexts(language: Language, labelKey: string, keywordsKey: string | undefined): string[] {
  const texts = [translate(other(language), labelKey)];
  if (keywordsKey) for (const lang of [language, other(language)]) texts.push(...keywordsOf(translate(lang, keywordsKey)));
  return texts;
}

/**
 * The commands the screen on show offers right now, as palette entries ("On this screen"). The
 * shell's own (ids "shell.") are the palette's Actions already. A command runs once the palette
 * has closed and given the focus back, so a command that moves the focus (to a form's first field,
 * a menu's first item) keeps it.
 */
export function commandEntries(commands: CommandDef[], t: Translate, language: Language): PaletteEntry[] {
  const seen = new Set<string>();
  const entries: PaletteEntry[] = [];
  for (const command of commands) {
    if (command.id.startsWith("shell.")) continue;
    // Two keys for one action (Ctrl+S and Ctrl+Enter both save) are one entry.
    const title = command.label ?? t(command.labelKey);
    if (seen.has(title)) continue;
    seen.add(title);
    entries.push({
      id: `here:${command.id}`,
      title,
      keywords: command.label ? [] : matchTexts(language, command.labelKey, command.keywordsKey),
      icon: "bolt",
      chord: command.chord,
      run: () => void window.setTimeout(command.run, 0),
    });
  }
  return entries;
}

/**
 * The module actions a user may start: those whose permission the user's roles grant and whose
 * screen the user may open (the same permission the navigation checks). Every other action is
 * not offered at all.
 */
export function offeredActions(actions: ModuleAction[], can: (permission: string) => boolean, routes: RouteDef[] = allRoutes): ModuleAction[] {
  return actions.filter((action) => {
    if (!action.permission || !can(action.permission)) return false;
    if (action.requires?.some((permission) => !can(permission))) return false;
    const route = matchRoute(action.path, routes);
    return Boolean(route) && (!route!.permission || can(route!.permission));
  });
}

/** Module actions as palette entries; `start` opens the action's screen and runs its command. */
export function actionEntries(actions: ModuleAction[], t: Translate, language: Language, start: (action: ModuleAction) => void): PaletteEntry[] {
  return actions.map((action) => ({
    id: `module:${action.key}`,
    title: t(action.labelKey),
    keywords: matchTexts(language, action.labelKey, action.keywordsKey),
    icon: "bolt",
    command: action.command ? { id: action.command, screen: action.path } : undefined,
    available: action.available,
    run: () => start(action),
  }));
}
