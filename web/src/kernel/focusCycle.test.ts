import { afterEach, describe, expect, it } from "vitest";
import { installFocusCycle, tabOrder } from "./focusCycle";

let remove: (() => void) | undefined;

afterEach(() => {
  remove?.();
  remove = undefined;
  document.body.innerHTML = "";
});

function press(shiftKey = false, init: KeyboardEventInit = {}): KeyboardEvent {
  const event = new KeyboardEvent("keydown", { key: "Tab", shiftKey, bubbles: true, cancelable: true, ...init });
  (document.activeElement ?? document.body).dispatchEvent(event);
  return event;
}

function page(html: string) {
  document.body.innerHTML = html;
  remove = installFocusCycle(document);
}

const id = () => (document.activeElement as HTMLElement | null)?.id;

describe("page-wide Tab cycle", () => {
  it("Tab on the last control goes to the first, with no stop on the document", () => {
    page('<a id="skip" href="#main">Skip</a><button id="b">B</button><a id="last" href="/me">Me</a>');
    document.getElementById("last")!.focus();
    const event = press();
    expect(event.defaultPrevented).toBe(true);
    expect(id()).toBe("skip");
  });

  it("Shift+Tab on the first control goes to the last", () => {
    page('<a id="skip" href="#main">Skip</a><button id="b">B</button><a id="last" href="/me">Me</a>');
    document.getElementById("skip")!.focus();
    expect(press(true).defaultPrevented).toBe(true);
    expect(id()).toBe("last");
  });

  it("leaves Tab between other controls to the browser", () => {
    page('<a id="skip" href="#main">Skip</a><button id="b">B</button><a id="last" href="/me">Me</a>');
    document.getElementById("b")!.focus();
    expect(press().defaultPrevented).toBe(false);
    expect(press(true).defaultPrevented).toBe(false);
    expect(id()).toBe("b");
  });

  it("skips what Tab never visits: disabled, hidden, inert, tabindex -1, hidden inputs", () => {
    page(`<button id="first">A</button><a id="last" href="/x">X</a>
      <button disabled>off</button><div hidden><button>hidden</button></div><div inert><a href="/i">inert</a></div>
      <button tabindex="-1">programmatic</button><input type="hidden" value="1" />`);
    expect(tabOrder(document)!.map((e) => e.id)).toEqual(["first", "last"]);
    document.getElementById("last")!.focus();
    press();
    expect(id()).toBe("first");
  });

  it("leaves a modal dialog's own cycle alone", () => {
    page('<button id="first">A</button><div role="dialog" aria-modal="true"><button id="last">OK</button></div>');
    document.getElementById("last")!.focus();
    expect(press().defaultPrevented).toBe(false);
    expect(id()).toBe("last");
  });

  it("leaves Ctrl+Tab, Alt+Tab and an already handled Tab to their owners", () => {
    page('<button id="first">A</button><a id="last" href="/x">X</a>');
    document.getElementById("last")!.focus();
    expect(press(false, { ctrlKey: true }).defaultPrevented).toBe(false);
    expect(press(false, { altKey: true }).defaultPrevented).toBe(false);
    const handled = new KeyboardEvent("keydown", { key: "Tab", bubbles: true, cancelable: true });
    handled.preventDefault();
    document.getElementById("last")!.dispatchEvent(handled);
    expect(id()).toBe("last");
  });

  it("lets the browser decide when a positive tabindex reorders the page", () => {
    page('<button id="first">A</button><a id="last" href="/x" tabindex="2">X</a>');
    expect(tabOrder(document)).toBeNull();
    document.getElementById("last")!.focus();
    expect(press().defaultPrevented).toBe(false);
  });

  it("counts a radio group as one stop", () => {
    page(`<form><button id="first" type="button">A</button>
      <input type="radio" name="r" id="r1" /><input type="radio" name="r" id="r2" checked /><input type="radio" name="r" id="r3" /></form>`);
    expect(tabOrder(document)!.map((e) => e.id)).toEqual(["first", "r2"]);
    document.getElementById("r2")!.focus();
    press();
    expect(id()).toBe("first");
  });

  it("stops when removed", () => {
    page('<button id="first">A</button><a id="last" href="/x">X</a>');
    remove!();
    remove = undefined;
    document.getElementById("last")!.focus();
    expect(press().defaultPrevented).toBe(false);
  });
});
