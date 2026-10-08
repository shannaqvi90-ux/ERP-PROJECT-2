import { act } from "react";
import { afterEach, describe, expect, it } from "vitest";
import { render, type Rendered } from "../test/render";
import { chordForAria, chordKeys, isEditable, matches, parseChord, ShortcutProvider, useShortcut, useShortcutList } from "./shortcuts";

let view: Rendered | undefined;
afterEach(() => view?.unmount());

const key = (init: Partial<KeyboardEventInit> & { code: string }) => new KeyboardEvent("keydown", { bubbles: true, cancelable: true, ...init });

describe("shortcut chords", () => {
  it("matches the key's position, so an Arabic keyboard layout works the same", () => {
    const chord = parseChord("Mod+KeyK");
    expect(matches(chord, key({ code: "KeyK", key: "ن", ctrlKey: true }), false)).toBe(true);
    expect(matches(chord, key({ code: "KeyK", key: "k", metaKey: true }), true)).toBe(true);
    expect(matches(chord, key({ code: "KeyK", key: "k", metaKey: true }), false)).toBe(false);
  });

  it("never captures AltGr (Ctrl+Alt) typing as an Alt shortcut", () => {
    const chord = parseChord("Alt+KeyL");
    expect(matches(chord, key({ code: "KeyL", altKey: true }), false)).toBe(true);
    expect(matches(chord, key({ code: "KeyL", altKey: true, ctrlKey: true }), false)).toBe(false);
  });

  it("refuses malformed chords", () => {
    expect(() => parseChord("Alt")).toThrow();
    expect(() => parseChord("Hyper+KeyK")).toThrow();
  });

  it("prints keys the way the keyboard does, per platform", () => {
    expect(chordKeys("Mod+KeyK", false)).toEqual(["Ctrl", "K"]);
    expect(chordKeys("Mod+KeyK", true)).toEqual(["⌘", "K"]);
    expect(chordKeys("Shift+Slash", false)).toEqual(["?"]);
    expect(chordKeys("Alt+KeyL", false)).toEqual(["Alt", "L"]);
    expect(chordForAria("Mod+KeyK", false)).toBe("Control+K");
  });

  it("knows which elements take typing", () => {
    const input = document.createElement("input");
    const checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    expect(isEditable(input)).toBe(true);
    expect(isEditable(checkbox)).toBe(false);
    expect(isEditable(document.createElement("textarea"))).toBe(true);
    expect(isEditable(document.createElement("button"))).toBe(false);
  });
});

function Probe({ onA, onB, chordB = "Shift+Slash" }: { onA: () => void; onB: () => void; chordB?: string }) {
  useShortcut({ id: "test.a", chord: "Alt+KeyL", labelKey: "a", groupKey: "g", run: onA });
  useShortcut({ id: "test.b", chord: chordB, labelKey: "b", groupKey: "g", run: onB });
  const list = useShortcutList();
  return <output>{list.map((s) => s.id).join(",")}</output>;
}

describe("shortcut registry", () => {
  it("runs shortcuts, lists them, and keeps plain keys out of text fields", async () => {
    let a = 0;
    let b = 0;
    view = await render(
      <ShortcutProvider>
        <Probe onA={() => a++} onB={() => b++} />
        <input />
      </ShortcutProvider>,
    );
    expect(view.container.querySelector("output")!.textContent).toBe("test.a,test.b");
    act(() => {
      window.dispatchEvent(key({ code: "KeyL", altKey: true }));
      document.body.dispatchEvent(key({ code: "Slash", shiftKey: true }));
    });
    expect([a, b]).toEqual([1, 1]);
    const input = view.container.querySelector("input")!;
    act(() => {
      input.dispatchEvent(key({ code: "Slash", shiftKey: true }));
      input.dispatchEvent(key({ code: "KeyL", altKey: true }));
    });
    expect([a, b]).toEqual([2, 1]);
  });

  it("only lets marked shortcuts through while a modal dialog is open", async () => {
    let a = 0;
    view = await render(
      <ShortcutProvider>
        <Probe onA={() => a++} onB={() => undefined} />
        <div aria-modal="true" />
      </ShortcutProvider>,
    );
    act(() => window.dispatchEvent(key({ code: "KeyL", altKey: true })));
    expect(a).toBe(0);
  });

  it("refuses two shortcuts on the same keys", async () => {
    const original = console.error;
    console.error = () => undefined;
    try {
      await expect(
        render(
          <ShortcutProvider>
            <Probe onA={() => undefined} onB={() => undefined} chordB="Alt+KeyL" />
          </ShortcutProvider>,
        ),
      ).rejects.toThrow(/already used/);
    } finally {
      console.error = original;
    }
  });
});
