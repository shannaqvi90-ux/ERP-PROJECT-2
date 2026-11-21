import { afterEach, describe, expect, it } from "vitest";
import { sweepOverflowTitles } from "./overflowTitles";

function cut(el: HTMLElement, scroll: number, client: number) {
  Object.defineProperty(el, "scrollWidth", { configurable: true, value: scroll });
  Object.defineProperty(el, "clientWidth", { configurable: true, value: client });
}

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
});
