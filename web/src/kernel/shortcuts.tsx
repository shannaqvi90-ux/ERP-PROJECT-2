import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";

/**
 * Global keyboard shortcuts. A shortcut is written as modifiers plus a physical key code
 * ("Mod+KeyK", "Alt+KeyL", "Shift+Slash"): matching uses KeyboardEvent.code, the key's position,
 * so every shortcut works the same with an Arabic keyboard layout, where KeyK types "ن".
 * "Mod" is Ctrl, or ⌘ on Apple devices. AltGr (Ctrl+Alt) never matches an Alt shortcut, so
 * typing characters with AltGr is never captured.
 *
 * Shortcuts without Alt/Ctrl/⌘ ("?") never fire while the user types in a field. While a modal
 * dialog is open only shortcuts marked `inDialogs` fire. Two active shortcuts may not share a
 * chord: registering a duplicate throws, so a clash fails the tests instead of shadowing a key.
 * Every registered shortcut is listed, with its translated label, in the shortcut help sheet.
 */
export type ShortcutDef = {
  id: string;
  /** "Mod+KeyK", "Alt+KeyL", "Shift+Slash", "Mod+Slash", "Alt+KeyM" … */
  chord: string;
  /** String key of what the shortcut does (shown in the help sheet). */
  labelKey: string;
  /** String key of the help sheet section. */
  groupKey: string;
  run: () => void;
  /** Also fire while a modal dialog is open (for example the palette's own toggle). */
  inDialogs?: boolean;
};

export type Chord = { mod: boolean; alt: boolean; shift: boolean; code: string };

const modifierNames = new Set(["Mod", "Alt", "Shift"]);

export function parseChord(chord: string): Chord {
  const parts = chord.split("+");
  const code = parts.pop() ?? "";
  if (!code || modifierNames.has(code)) throw new Error(`Shortcut "${chord}" has no key`);
  for (const part of parts) if (!modifierNames.has(part)) throw new Error(`Shortcut "${chord}": unknown modifier "${part}"`);
  return { mod: parts.includes("Mod"), alt: parts.includes("Alt"), shift: parts.includes("Shift"), code };
}

export const isApple = (): boolean =>
  typeof navigator !== "undefined" && /Mac|iPhone|iPad|iPod/.test(navigator.platform || navigator.userAgent || "");

type KeyLike = Pick<KeyboardEvent, "code" | "ctrlKey" | "metaKey" | "altKey" | "shiftKey">;

export function matches(chord: Chord, event: KeyLike, apple = isApple()): boolean {
  if (event.code !== chord.code) return false;
  const mod = apple ? event.metaKey : event.ctrlKey;
  const other = apple ? event.ctrlKey : event.metaKey;
  return mod === chord.mod && !other && event.altKey === chord.alt && event.shiftKey === chord.shift;
}

const keyCaptions: Record<string, string> = {
  Slash: "/",
  Period: ".",
  Comma: ",",
  Escape: "Esc",
  Enter: "Enter",
  Space: "Space",
  ArrowUp: "↑",
  ArrowDown: "↓",
  ArrowLeft: "←",
  ArrowRight: "→",
  Home: "Home",
  End: "End",
  PageUp: "PgUp",
  PageDown: "PgDn",
  Tab: "Tab",
  Backspace: "⌫",
};

function keyCaption(code: string, shift: boolean): string {
  if (code === "Slash" && shift) return "?";
  if (/^Key[A-Z]$/.test(code)) return code.slice(3);
  if (/^Digit\d$/.test(code)) return code.slice(5);
  if (/^F\d{1,2}$/.test(code)) return code;
  return keyCaptions[code] ?? code;
}

/** The keys to press, as printed on a keyboard: ["Ctrl", "K"] or ["⌘", "K"]; "?" for Shift+Slash. */
export function chordKeys(chord: string, apple = isApple()): string[] {
  const c = parseChord(chord);
  const keys: string[] = [];
  if (c.mod) keys.push(apple ? "⌘" : "Ctrl");
  if (c.alt) keys.push(apple ? "⌥" : "Alt");
  if (c.shift && !(c.code === "Slash")) keys.push(apple ? "⇧" : "Shift");
  keys.push(keyCaption(c.code, c.shift));
  return keys;
}

/** The same chord as a Playwright/aria key string ("Control+K", "Alt+L", "Shift+?"). */
export function chordForAria(chord: string, apple = isApple()): string {
  const c = parseChord(chord);
  const keys: string[] = [];
  if (c.mod) keys.push(apple ? "Meta" : "Control");
  if (c.alt) keys.push("Alt");
  if (c.shift) keys.push("Shift");
  keys.push(keyCaption(c.code, c.shift));
  return keys.join("+");
}

export function isEditable(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  if (target.isContentEditable) return true;
  const tag = target.tagName;
  if (tag === "TEXTAREA" || tag === "SELECT") return true;
  if (tag !== "INPUT") return false;
  const type = (target as HTMLInputElement).type;
  return !["button", "checkbox", "radio", "submit", "reset", "range", "color", "file"].includes(type);
}

const modalOpen = (): boolean => typeof document !== "undefined" && document.querySelector('[aria-modal="true"]') !== null;

type Registered = ShortcutDef & { parsed: Chord };

type Registry = {
  register: (def: ShortcutDef) => () => void;
  list: () => ShortcutDef[];
  version: number;
};

const ShortcutContext = createContext<Registry | null>(null);

/** One registry and one key listener for the whole app. */
export function ShortcutProvider({ children }: { children: ReactNode }) {
  const entries = useRef(new Map<string, Registered>());
  const [version, setVersion] = useState(0);

  const register = useCallback((def: ShortcutDef) => {
    const parsed = parseChord(def.chord);
    for (const other of entries.current.values()) {
      if (other.id !== def.id && other.chord === def.chord) {
        throw new Error(`Shortcut ${def.chord} of "${def.id}" is already used by "${other.id}"`);
      }
    }
    entries.current.set(def.id, { ...def, parsed });
    setVersion((v) => v + 1);
    return () => {
      if (entries.current.get(def.id)?.run === def.run) entries.current.delete(def.id);
      setVersion((v) => v + 1);
    };
  }, []);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.defaultPrevented || event.isComposing || event.repeat) return;
      const apple = isApple();
      for (const entry of entries.current.values()) {
        if (!matches(entry.parsed, event, apple)) continue;
        const plain = !entry.parsed.mod && !entry.parsed.alt;
        if (plain && isEditable(event.target)) return;
        if (modalOpen() && !entry.inDialogs) return;
        event.preventDefault();
        event.stopPropagation();
        entry.run();
        return;
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  const value = useMemo<Registry>(
    () => ({ register, list: () => [...entries.current.values()].map(({ parsed: _parsed, ...def }) => def), version }),
    [register, version],
  );
  return <ShortcutContext.Provider value={value}>{children}</ShortcutContext.Provider>;
}

function useRegistry(): Registry {
  const registry = useContext(ShortcutContext);
  if (!registry) throw new Error("Shortcuts need a ShortcutProvider");
  return registry;
}

/** Register a shortcut while the calling component is mounted. `run` may change between renders. */
export function useShortcut(def: Omit<ShortcutDef, "run"> & { run: () => void; enabled?: boolean }): void {
  const { register } = useRegistry();
  const run = useRef(def.run);
  run.current = def.run;
  const { id, chord, labelKey, groupKey, inDialogs, enabled = true } = def;
  useEffect(() => {
    if (!enabled) return;
    const stable = () => run.current();
    return register({ id, chord, labelKey, groupKey, inDialogs, run: stable });
  }, [register, id, chord, labelKey, groupKey, inDialogs, enabled]);
}

/** Every active shortcut (re-renders when one is added or removed). */
export function useShortcutList(): ShortcutDef[] {
  const { list, version } = useRegistry();
  // `version` changes whenever the registry does.
  return useMemo(() => list(), [list, version]);
}

/** Shows a chord the way the keyboard prints it; always left to right, also on Arabic screens. */
export function Keys({ chord, className }: { chord: string; className?: string }) {
  const keys = chordKeys(chord);
  return (
    <span className={className ? `keys ${className}` : "keys"} dir="ltr">
      {keys.map((key, i) => (
        <kbd key={i}>{key}</kbd>
      ))}
    </span>
  );
}
