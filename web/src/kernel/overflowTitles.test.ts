import { afterEach, describe, expect, it, vi } from "vitest";
import { createElement } from "react";
import { act } from "react";
import { createRoot } from "react-dom/client";
import { installOverflowTitles, sweepOverflowTitles, useOverflowTitles } from "./overflowTitles";

function cut(el: HTMLElement, scroll: number, client: number) {
  Object.defineProperty(el, "scrollWidth", { configurable: true, value: scroll });
  Object.defineProperty(el, "clientWidth", { configurable: true, value: client });
}

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe("overflow titles", () => {
  afterEach(() => (document.body.innerHTML = ""));

  it("gives a cut cell its full text as a title, in English and Arabic", () => {
    document.body.innerHTML = `<table><tr><td id="a">Al Noor Electromechanical LLC 15</td><td id="b">شركة النور للأعمال الكهروميكانيكية</td><td id="c">Short</td></tr></table>`;
    cut(document.getElementById("a")!, 300, 120);
    cut(document.getElementById("b")!, 300, 120);
    cut(document.getElementById("c")!, 40, 120);
    expect(sweepOverflowTitles()).toBe(2);
    expect(document.getElementById("a")!.title).toBe("Al Noor Electromechanical LLC 15");
    expect(document.getElementById("b")!.title).toBe("شركة النور للأعمال الكهروميكانيكية");
    expect(document.getElementById("c")!.title).toBe("");
  });

  it("keeps a title a component set itself, and removes its own when the text fits again", () => {
    document.body.innerHTML = `<table><tr><td id="a" title="Own">Long long long</td><td id="b">Long long</td></tr></table>`;
    cut(document.getElementById("a")!, 300, 100);
    cut(document.getElementById("b")!, 300, 100);
    sweepOverflowTitles();
    expect(document.getElementById("a")!.title).toBe("Own");
    expect(document.getElementById("b")!.title).toBe("Long long");
    cut(document.getElementById("b")!, 50, 100);
    sweepOverflowTitles();
    expect(document.getElementById("b")!.hasAttribute("title")).toBe(false);
  });

  it("titles a cut input value", () => {
    document.body.innerHTML = `<input id="i" value="A very long licensing authority name" />`;
    cut(document.getElementById("i")!, 400, 120);
    sweepOverflowTitles();
    expect(document.getElementById("i")!.title).toBe("A very long licensing authority name");
  });

  it("titles a cut cell in the render that shows it and when it is focused, and the page changes by itself never", async () => {
    const stop = installOverflowTitles();
    const holder = document.createElement("div");
    document.body.appendChild(holder);
    const cell = document.createElement("td");
    cell.textContent = "Al Noor Electromechanical LLC 15";
    cut(cell, 300, 120);
    const row = document.createElement("tr");
    row.appendChild(cell);
    const table = document.createElement("table");
    table.appendChild(row);
    document.body.appendChild(table);
    function Screen() {
      useOverflowTitles();
      return null;
    }
    try {
      await new Promise(r => setTimeout(r, 300));
      expect(cell.hasAttribute("title")).toBe(false);
      const root = createRoot(holder);
      await act(async () => root.render(createElement(Screen)));
      expect(cell.title).toBe("Al Noor Electromechanical LLC 15");
      cut(cell, 50, 120);
      await act(async () => root.render(createElement(Screen, { key: "again" })));
      cell.dispatchEvent(new Event("pointermove", { bubbles: true }));
      expect(cell.hasAttribute("title")).toBe(false);
      cut(cell, 300, 120);
      cell.dispatchEvent(Object.assign(new Event("pointermove", { bubbles: true }), { movementX: 3, movementY: 1 }));
      expect(cell.hasAttribute("title")).toBe(false);
      cell.dispatchEvent(new Event("focusin", { bubbles: true }));
      expect(cell.title).toBe("Al Noor Electromechanical LLC 15");
      root.unmount();
    } finally {
      stop();
    }
  });

  it("starts no observer and no timer", () => {
    const observe = vi.spyOn(MutationObserver.prototype, "observe");
    const timers = vi.spyOn(window, "setTimeout");
    const stop = installOverflowTitles();
    stop();
    expect(observe).not.toHaveBeenCalled();
    expect(timers).not.toHaveBeenCalled();
    vi.restoreAllMocks();
  });
});
