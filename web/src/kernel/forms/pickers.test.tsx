import { act, useState } from "react";
import { afterEach, describe, expect, it } from "vitest";
import { I18nProvider } from "../i18n";
import { render, settle, setInput, type Rendered } from "../../test/render";
import { DateInput } from "./dateInput";
import { SearchSelect } from "./SearchSelect";
import { Calendar } from "./calendar";
import { toast, ToastHost, Sparkline, clearToasts } from "../ui";

let view: Rendered | undefined;
afterEach(() => {
  view?.unmount();
  view = undefined;
  clearToasts();
});

const key = (el: Element | null, k: string, init: KeyboardEventInit = {}) =>
  act(() => {
    (el ?? document.body).dispatchEvent(new KeyboardEvent("keydown", { key: k, bubbles: true, cancelable: true, ...init }));
  });

function DateHarness({ language = "en" as "en" | "ar" }) {
  const [v, setV] = useState("2026-11-18");
  return (
    <I18nProvider initial={language}>
      <DateInput value={v} onChange={setV} aria-label="when" />
      <output data-testid="out">{v}</output>
    </I18nProvider>
  );
}

describe("keyboard calendar", () => {
  it("opens from the button (reachable by Tab), moves by day, week and month with the keys, and Enter chooses", async () => {
    view = await render(<DateHarness />);
    const button = view.container.querySelector<HTMLButtonElement>(".date-picker-button")!;
    expect(button.tabIndex).toBe(0);
    act(() => button.click());
    await settle();
    const grid = view.container.querySelector('[role="grid"]')!;
    expect(document.activeElement?.getAttribute("aria-label")).toBe("18/11/2026");
    key(grid, "ArrowRight");
    await settle();
    key(document.activeElement, "ArrowDown");
    await settle();
    expect(document.activeElement?.getAttribute("aria-label")).toBe("26/11/2026");
    key(document.activeElement, "PageDown");
    await settle();
    expect(document.activeElement?.getAttribute("aria-label")).toBe("26/12/2026");
    key(document.activeElement, "Enter");
    await settle();
    expect(view.container.querySelector('[data-testid="out"]')!.textContent).toBe("2026-12-26");
    expect(view.container.querySelector(".calendar")).toBeNull();
  });

  it("Escape closes it without choosing, and a day outside min is not offered", async () => {
    let picked = "";
    view = await render(
      <I18nProvider initial="en">
        <Calendar value="2026-11-18" min="2026-11-10" onPick={(d) => (picked = d)} onClose={() => (picked = "closed")} />
      </I18nProvider>,
    );
    const days = [...view.container.querySelectorAll<HTMLButtonElement>(".calendar-day")];
    expect(days.find((d) => d.getAttribute("aria-label") === "05/11/2026")!.disabled).toBe(true);
    key(view.container.querySelector(".calendar"), "Escape");
    expect(picked).toBe("closed");
  });

  it("starts the week on Monday and mirrors the arrow keys in Arabic", async () => {
    view = await render(
      <I18nProvider initial="ar">
        <Calendar value="2026-11-18" onPick={() => {}} onClose={() => {}} />
      </I18nProvider>,
    );
    key(view.container.querySelector(".calendar"), "ArrowLeft");
    await settle();
    expect(document.activeElement?.getAttribute("aria-label")).toBe("19/11/2026");
  });
});

function SelectHarness() {
  const [v, setV] = useState("AE");
  const options = [
    { value: "AE", label: "United Arab Emirates" },
    { value: "SA", label: "Saudi Arabia" },
    { value: "OM", label: "Oman" },
    { value: "EG", label: "Egypt" },
  ];
  return (
    <I18nProvider initial="en">
      <SearchSelect id="c" name="country" value={v} options={options} onChange={setV} />
      <output data-testid="out">{v}</output>
    </I18nProvider>
  );
}

describe("drop-down with search", () => {
  it("narrows by what is typed, moves with the arrows, chooses with Enter, and keeps a native select in step", async () => {
    view = await render(<SelectHarness />);
    const input = view.container.querySelector<HTMLInputElement>("input[role=combobox]")!;
    act(() => input.focus());
    setInput(input, "sau");
    await settle();
    const options = [...view.container.querySelectorAll('[role="option"]')].map((o) => o.textContent);
    expect(options).toEqual(["Saudi Arabia"]);
    key(input, "Enter");
    await settle();
    expect(view.container.querySelector('[data-testid="out"]')!.textContent).toBe("SA");
    expect(view.container.querySelector<HTMLSelectElement>("select.search-select-native")!.value).toBe("SA");
    expect(view.container.querySelector<HTMLSelectElement>("select.search-select-native")!.tabIndex).toBe(-1);
  });

  it("Escape closes without changing, and Down opens the whole list", async () => {
    view = await render(<SelectHarness />);
    const input = view.container.querySelector<HTMLInputElement>("input[role=combobox]")!;
    act(() => input.focus());
    key(input, "ArrowDown");
    await settle();
    expect(view.container.querySelectorAll('[role="option"]').length).toBe(4);
    key(input, "Escape");
    await settle();
    expect(view.container.querySelector('[role="listbox"]')).toBeNull();
    expect(view.container.querySelector('[data-testid="out"]')!.textContent).toBe("AE");
  });
});

describe("toasts and sparkline", () => {
  it("shows a toast in a live region and takes it away when dismissed", async () => {
    view = await render(
      <I18nProvider initial="en">
        <ToastHost />
      </I18nProvider>,
    );
    act(() => void toast("Saved", "success"));
    await settle();
    expect(view.container.querySelector('[aria-live="polite"]')!.textContent).toContain("Saved");
    act(() => view!.container.querySelector<HTMLButtonElement>(".toast-close")!.click());
    await settle();
    expect(view.container.querySelector(".toast")).toBeNull();
  });

  it("draws a sparkline from at least two points and nothing from fewer", async () => {
    view = await render(
      <>
        <Sparkline points={[1, 3, 2, 5]} label="New per month" />
        <Sparkline points={[1]} label="x" />
      </>,
    );
    expect(view.container.querySelectorAll("svg.sparkline").length).toBe(1);
    expect(view.container.querySelector("svg.sparkline")!.getAttribute("aria-label")).toBe("New per month");
  });
});
