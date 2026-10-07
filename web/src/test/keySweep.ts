import { act } from "react";
import { settle } from "./render";

// G2 on screen, by keyboard (critic p06 round 2, plant W2: the record form's Ctrl+S left on and
// save() no longer checking canEdit, so a read-only user's Ctrl+S sent a PUT while every screen gate
// passed). The product is keyboard-first: its shortcuts are its main controls, so a screen gate that
// only looks at the buttons drawn misses an action the keys still offer. This sweep presses every
// key a person can press, alone and with every modifier, on a screen whose user lacks a write
// permission, accepts whatever dialog or menu a key opened (as Enter on its focused button would),
// and reports every request other than a read that left meanwhile. It does not need to know which
// shortcuts exist: a shortcut added later is pressed too.

/** A sweep presses some thousands of keys and activates every control: its time limit is its own,
 * not the 5 s default meant for one interaction (about 2 to 6 s each on a quiet machine). */
export const sweepTimeLimit = 60_000;

export type Call = { method: string; url: string };
export type Write = Call & { key: string };

/** Physical keys (KeyboardEvent.code) and the character or name each types without Shift. */
const keys: [code: string, key: string][] = [
  ..."ABCDEFGHIJKLMNOPQRSTUVWXYZ".split("").map((c): [string, string] => [`Key${c}`, c.toLowerCase()]),
  ..."0123456789".split("").map((d): [string, string] => [`Digit${d}`, d]),
  ["Enter", "Enter"],
  ["NumpadEnter", "Enter"],
  ["Escape", "Escape"],
  ["Delete", "Delete"],
  ["Backspace", "Backspace"],
  ["Insert", "Insert"],
  ["Space", " "],
  ["Tab", "Tab"],
  ["Slash", "/"],
  ["Backslash", "\\"],
  ["Period", "."],
  ["Comma", ","],
  ["Minus", "-"],
  ["Equal", "="],
  ["Semicolon", ";"],
  ["Quote", "'"],
  ["Backquote", "`"],
  ["BracketLeft", "["],
  ["BracketRight", "]"],
  ["PageUp", "PageUp"],
  ["PageDown", "PageDown"],
  ["Home", "Home"],
  ["End", "End"],
  ["ArrowUp", "ArrowUp"],
  ["ArrowDown", "ArrowDown"],
  ["ArrowLeft", "ArrowLeft"],
  ["ArrowRight", "ArrowRight"],
  ...Array.from({ length: 12 }, (_, i): [string, string] => [`F${i + 1}`, `F${i + 1}`]),
];

const shifted: Record<string, string> = { Slash: "?", Period: ">", Comma: "<", Minus: "_", Equal: "+", Semicolon: ":", Quote: '"', Backquote: "~", BracketLeft: "{", BracketRight: "}", Backslash: "|" };

/** Ctrl stands for the platform's command key (Ctrl here; the test DOM is not an Apple device).
 * Ctrl+Alt is AltGr, which types characters and is never a shortcut. */
const modifierSets: { name: string; init: KeyboardEventInit }[] = [
  { name: "", init: {} },
  { name: "Ctrl+", init: { ctrlKey: true } },
  { name: "Alt+", init: { altKey: true } },
  { name: "Shift+", init: { shiftKey: true } },
  { name: "Ctrl+Shift+", init: { ctrlKey: true, shiftKey: true } },
  { name: "Alt+Shift+", init: { altKey: true, shiftKey: true } },
];

export type Chord = { name: string; init: KeyboardEventInit };

/** Every key alone and with every modifier: the keys a person can press. */
export function everyChord(): Chord[] {
  const out: Chord[] = [];
  for (const mods of modifierSets) {
    for (const [code, plain] of keys) {
      const key = mods.init.shiftKey ? (shifted[code] ?? (plain.length === 1 ? plain.toUpperCase() : plain)) : plain;
      out.push({ name: `${mods.name}${code}`, init: { ...mods.init, code, key } });
    }
  }
  return out;
}

/**
 * The writes a user who may only read a screen can still make: the caller's own settings, signing
 * out, and personal saved views of a list the user may read (each needs no permission beyond
 * reading that list). Everything else is a write the sweep reports.
 */
export function readerMayWrite({ method, url }: Call): boolean {
  const path = new URL(url, "http://localhost").pathname;
  if (method === "PUT" && path === "/api/identity/me/preferences") return true;
  if (method === "POST" && path === "/api/auth/sign-out") return true;
  if (/^\/api\/lists\/[\w.-]+\/views(\/[\w-]+)?$/.test(path)) return true;
  return false;
}

/** Lets the screen settle until no request has left for two turns of the event loop and `ready`
 * holds (at most `max` turns): a screen fetches in steps (definition, rows, record). */
export async function settleUntilQuiet(calls: () => readonly Call[], ready: () => boolean = () => true, max = 40): Promise<void> {
  let quiet = 0;
  for (let turn = 0; turn < max && (quiet < 2 || !ready()); turn++) {
    const before = calls().length;
    await settle();
    quiet = calls().length === before ? quiet + 1 : 0;
  }
}

const writes = (calls: readonly Call[], from: number) => calls.slice(from).filter((c) => c.method !== "GET" && c.method !== "HEAD");

function press(target: EventTarget, init: KeyboardEventInit) {
  act(() => {
    target.dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, ...init }));
    target.dispatchEvent(new KeyboardEvent("keyup", { bubbles: true, cancelable: true, ...init }));
  });
}

/** Whatever a key opened that asks for a choice: modal dialogs and menus. */
const openChoices = () => [...document.querySelectorAll<HTMLElement>('[aria-modal="true"], [role="menu"]')];

/** Accepts what a key opened, as Enter on its focused control would: the focused button, else the
 * dialog's primary button, else its first button. Links are not followed (they only read). */
async function acceptChoices() {
  for (let round = 0; round < 3; round++) {
    const open = openChoices();
    if (open.length === 0) return;
    const top = open[open.length - 1]!;
    const focused = document.activeElement;
    const button =
      (focused instanceof HTMLButtonElement && top.contains(focused) && !focused.disabled ? focused : null) ??
      top.querySelector<HTMLButtonElement>("button.primary:not([disabled])") ??
      top.querySelector<HTMLButtonElement>("button:not([disabled])");
    if (!button) break;
    act(() => button.click());
    await settle();
  }
  // Close whatever is still open, so the next key meets the screen itself.
  for (let round = 0; round < 3 && openChoices().length > 0; round++) {
    const top = openChoices().at(-1)!;
    press(top.contains(document.activeElement) ? document.activeElement! : top, { key: "Escape", code: "Escape" });
    await settle();
  }
}

/** A state of the screen the sweep presses every key in, besides the screen as shown: rows chosen on
 * a list, say, where the bulk actions appear. `enter` puts the screen in that state from wherever it
 * is (it is called again before every key, as a key may have left the state). */
export type SweepState = { name: string; enter: () => Promise<void> | void };

export type SweepOptions = {
  /** The fetch log of the screen under test (from mockFetch). */
  calls: readonly Call[];
  /** Whether the screen under test is still the one shown (a key may close it or move away). */
  shown: () => boolean;
  /** Shows the screen again, from scratch; returns the new fetch log. */
  reopen: () => Promise<readonly Call[]>;
  /** Where focus is put before each key; null leaves it on the page body. Each target gets every key. */
  targets: (() => HTMLElement | null)[];
  /** Writes this user may make anyway (default: readerMayWrite). */
  allowed?: (call: Call) => boolean;
  /** States to sweep in besides the screen as shown (default: none). */
  states?: SweepState[];
  /** Something on screen the user may not do though no request left (a new-record form opened for a
   * user who may not create one, say): its description, or null. Reported with method "OFFERED". */
  forbidden?: () => string | null;
  /** Sweep everything even when ERP_SWEEP_FIRST_FIND=1 (the plant self-test's setting, under which a
   * sweep stops at its first find): for the controls, which must find every write they expect. */
  exhaustive?: boolean;
};

/**
 * Presses every key, alone and with every modifier, on each focus target of the screen, accepts
 * any dialog or menu it opened, and returns every write that left (method, address and the key that
 * sent it). Then activates (Enter and Space, and a click, as a browser would) every control the
 * screen lets the keyboard reach. A screen that only reads returns [].
 */
export async function sweepKeys(options: SweepOptions): Promise<Write[]> {
  const allowed = options.allowed ?? readerMayWrite;
  let calls = options.calls;
  const found: Write[] = [];
  const record = (from: number, key: string) => {
    for (const w of writes(calls, from)) if (!allowed(w)) found.push({ key, method: w.method, url: w.url });
    const offered = options.forbidden?.();
    if (offered && !found.some((f) => f.method === "OFFERED" && f.url === offered && f.key === key)) found.push({ key, method: "OFFERED", url: offered });
  };
  const ensureShown = async () => {
    if (!options.shown()) {
      calls = await options.reopen();
    }
  };
  const states: SweepState[] = [{ name: "", enter: () => undefined }, ...(options.states ?? [])];
  // Controls already activated in an earlier state are not activated again in a later one.
  const known = new Set<string>();
  // A plant self-test only needs to know that something was found: it may stop at the first find.
  const firstOnly = !options.exhaustive && typeof process !== "undefined" && process.env.ERP_SWEEP_FIRST_FIND === "1";
  const done = () => firstOnly && found.length > 0;
  for (const state of states) {
    const where = (focus: string) => `(${state.name ? `${state.name}, ` : ""}focus ${focus})`;
    const enter = async () => {
      await ensureShown();
      if (state.name) {
        await state.enter();
        await settle();
      }
    };
    for (const [t, target] of options.targets.entries()) {
      for (const chord of everyChord()) {
        if (done()) return found;
        await enter();
        const el = target();
        el?.focus();
        const from = calls.length;
        press(el ?? document.activeElement ?? document.body, chord.init);
        await settle();
        const offered = options.forbidden?.();
        if (offered) found.push({ key: `${chord.name} ${where(`${t + 1}: ${el ? describe(el) : "page"}`)}`, method: "OFFERED", url: offered });
        await acceptChoices();
        record(from, `${chord.name} ${where(`${t + 1}: ${el ? describe(el) : "page"}`)}`);
      }
    }
    // Every control the keyboard can reach on the screen in this state, and every control that one
    // reveals (a tab's page, a menu's items, an expanded section): Enter and Space on it, as a
    // browser turns them into a click on a button. Controls are told apart by their name and, among
    // controls of the same name, their place.
    const fresh = async () => {
      calls = await options.reopen();
      if (state.name) {
        await state.enter();
        await settle();
      }
    };
    const reachable = () => {
      const seen = new Map<string, number>();
      return [...document.querySelectorAll<HTMLElement>("main button:not([disabled]), main [tabindex]:not([tabindex='-1'])")]
        .filter((el) => !el.closest("fieldset:disabled"))
        .map((el) => {
          const name = describe(el);
          const n = seen.get(name) ?? 0;
          seen.set(name, n + 1);
          return { el, id: n === 0 ? name : `${name} #${n + 1}` };
        });
    };
    const activate = (el: HTMLElement) => {
      el.focus();
      press(el, { key: "Enter", code: "Enter" });
      press(el, { key: " ", code: "Space" });
      if (el instanceof HTMLButtonElement && el.isConnected && !el.disabled) act(() => el.click());
    };
    /** Activates the controls of `path` in turn (each revealed by the one before); the last one's
     * element, or null when one of them is not on screen. */
    const reach = async (path: string[]) => {
      let el: HTMLElement | undefined;
      for (const [i, id] of path.entries()) {
        el = reachable().find((c) => c.id === id)?.el;
        if (!el) return null;
        if (i < path.length - 1) {
          activate(el);
          await settle();
        }
      }
      return el ?? null;
    };
    await fresh();
    const queue: string[][] = [];
    for (const { id } of reachable()) {
      if (known.has(id)) continue;
      known.add(id);
      queue.push([id]);
    }
    while (queue.length > 0 && !done()) {
      const path = queue.shift()!;
      await enter();
      const from = calls.length;
      let el = await reach(path);
      if (!el) {
        await fresh();
        el = await reach(path);
      }
      if (!el) continue;
      activate(el);
      await settle();
      if (path.length === 1) {
        for (const { id } of reachable()) {
          if (known.has(id)) continue;
          known.add(id);
          queue.push([...path, id]);
        }
      }
      await acceptChoices();
      record(from, `Enter on ${path.join(" > ")}${state.name ? ` (${state.name})` : ""}`);
    }
  }
  return found;
}

function describe(el: Element): string {
  const label = el.getAttribute("aria-label") ?? el.textContent?.trim().slice(0, 40) ?? "";
  return `${el.tagName.toLowerCase()}${label ? ` "${label}"` : ""}`;
}
