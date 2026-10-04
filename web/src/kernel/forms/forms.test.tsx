import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { api } from "../api";
import { I18nProvider } from "../i18n";
import { navigate } from "../router";
import { ShortcutProvider } from "../shortcuts";
import { mockFetch, render, settle, setInput, type Rendered } from "../../test/render";
import { DecimalField, decimalInput, TextField } from "./fields";
import { clearLeaveGuards, nothingUnsaved } from "./leave";
import { FormSection, RecordForm } from "./RecordForm";
import { useRecordForm } from "./useRecordForm";

let view: Rendered | undefined;

beforeEach(() => {
  localStorage.clear();
  clearLeaveGuards();
  window.history.replaceState(null, "", "/things");
});

afterEach(() => {
  view?.unmount();
  view = undefined;
  vi.restoreAllMocks();
});

type Thing = { id: string; name: string; amount: string; version: number };
type Draft = { name: string; amount: string };

function ThingForm({ canEdit = true, onClose = () => undefined, nav }: { canEdit?: boolean; onClose?: () => void; nav?: { next?: () => void; previous?: () => void; position?: number; total?: number } }) {
  const form = useRecordForm<Thing, Draft>({
    load: (signal) => api<Thing>("GET", "/api/things/t1", undefined, { signal }),
    initial: (t) => ({ name: t?.name ?? "", amount: t?.amount ?? "" }),
    canEdit,
    save: (draft, thing) => api<Thing>("PUT", "/api/things/t1", { ...draft, version: thing?.version }),
  }, "t1");
  return (
    <RecordForm form={form} title={form.record?.name ?? ""} onClose={onClose} nav={nav} document={{ report: "test.thing", parameter: "thing", id: "t1" }}>
      <FormSection title="Main">
        <TextField field={form.bind("name")} label="Name" />
        <DecimalField field={form.bind("amount")} label="Amount" scale={2} />
      </FormSection>
    </RecordForm>
  );
}

async function show(ui: React.ReactNode, language: "en" | "ar" = "en") {
  view = await render(
    <I18nProvider initial={language}>
      <ShortcutProvider>{ui}</ShortcutProvider>
    </I18nProvider>,
  );
  await settle();
  await settle();
  return view;
}

function press(init: KeyboardEventInit, target: EventTarget = document.activeElement ?? window) {
  act(() => {
    target.dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, ...init }));
  });
}

const input = (name: string) => view!.container.querySelector<HTMLInputElement>(`[data-field="${name}"] input`)!;
const buttons = () => [...view!.container.querySelectorAll("button")].map((b) => b.textContent);
const thing = { id: "t1", name: "Desk", amount: "12.50", version: 3 };

describe("the record form", () => {
  it("tracks unsaved changes, guards leaving, saves with Ctrl+S and maps the server's field errors to their fields", async () => {
    let puts = 0;
    const calls = mockFetch((method, url) => {
      if (method === "GET" && url === "/api/things/t1") return { status: 200, body: thing };
      if (method === "PUT") {
        puts++;
        return puts === 1
          ? { status: 400, body: { code: "validation", title: "Some fields need attention.", errors: { amount: [{ code: "range", message: "Too large." }], owner: [{ code: "x", message: "The owner left." }] } } }
          : { status: 200, body: { ...thing, name: "Desk (oak)", version: 4 } };
      }
      return { status: 404, body: {} };
    });
    await show(<ThingForm />);
    expect(input("name").value).toBe("Desk");
    expect(buttons()).not.toContain("Discard changes");
    expect(nothingUnsaved()).toBe(true);

    setInput(input("name"), "Desk (oak)");
    expect(view!.container.querySelector("h2")!.textContent).toContain("Unsaved changes");
    expect(buttons()).toContain("Discard changes");
    expect(nothingUnsaved()).toBe(false);

    // Leaving the screen asks first; refusing keeps everything where it is.
    const confirm = vi.fn(() => false);
    window.confirm = confirm;
    navigate("/elsewhere");
    expect(confirm).toHaveBeenCalledOnce();
    expect(window.location.pathname).toBe("/things");

    press({ ctrlKey: true, key: "s", code: "KeyS" });
    await settle();
    expect(input("amount").getAttribute("aria-invalid")).toBe("true");
    expect(view!.container.querySelector('[data-field="amount"] .field-error')!.textContent).toBe("Too large.");
    // A field not on the form is reported in the form's message.
    expect(view!.container.querySelector('.alert[role="alert"]')!.textContent).toContain("The owner left.");
    // The cursor goes to the first field the server refused.
    expect(document.activeElement).toBe(input("amount"));

    // Ctrl+Enter saves too, the S of Ctrl+S matched by key position (Arabic layout types "س").
    press({ ctrlKey: true, key: "س", code: "KeyS" });
    await settle();
    expect(calls.filter((c) => c.method === "PUT")).toHaveLength(2);
    expect(calls.filter((c) => c.method === "PUT")[1]!.body).toEqual({ name: "Desk (oak)", amount: "12.50", version: 3 });
    expect(view!.container.querySelector('.notice[role="status"]')!.textContent).toBe("Saved.");
    expect(nothingUnsaved()).toBe(true);
    expect(buttons()).not.toContain("Discard changes");
  });

  it("discards with Alt+Z and asks before closing a form with changes: save, discard or keep editing", async () => {
    mockFetch((method, url) => (method === "GET" && url === "/api/things/t1" ? { status: 200, body: thing } : { status: 404, body: {} }));
    const onClose = vi.fn();
    await show(<ThingForm onClose={onClose} />);
    setInput(input("name"), "Chair");
    press({ altKey: true, key: "z", code: "KeyZ" });
    await settle();
    expect(input("name").value).toBe("Desk");

    setInput(input("name"), "Chair");
    press({ key: "Escape", code: "Escape" }, input("name"));
    await settle();
    const dialog = document.querySelector('[role="dialog"]')!;
    expect(dialog.textContent).toContain("This record has changes that are not saved yet.");
    const keep = [...dialog.querySelectorAll("button")].find((b) => b.textContent === "Keep editing")!;
    act(() => keep.click());
    expect(onClose).not.toHaveBeenCalled();
    expect(input("name").value).toBe("Chair");

    press({ key: "Escape", code: "Escape" }, input("name"));
    await settle();
    const discard = [...document.querySelectorAll('[role="dialog"] button')].find((b) => b.textContent === "Discard changes") as HTMLButtonElement;
    act(() => discard.click());
    expect(onClose).toHaveBeenCalledOnce();
  });

  it("offers a stale record's latest version after a conflict", async () => {
    let gets = 0;
    mockFetch((method, url) => {
      if (method === "GET" && url === "/api/things/t1") {
        gets++;
        return { status: 200, body: gets === 1 ? thing : { ...thing, name: "Desk (changed elsewhere)", version: 9 } };
      }
      if (method === "PUT") return { status: 409, body: { code: "concurrency", title: "Someone else changed this record." } };
      return { status: 404, body: {} };
    });
    await show(<ThingForm />);
    setInput(input("name"), "Mine");
    press({ ctrlKey: true, key: "Enter", code: "Enter" });
    await settle();
    const latest = [...view!.container.querySelectorAll("button")].find((b) => b.textContent === "Show the latest version")!;
    await act(async () => latest.click());
    await settle();
    expect(input("name").value).toBe("Desk (changed elsewhere)");
  });

  it("shows a record read-only to a user who may not change it: no save, fields disabled, the reason said", async () => {
    mockFetch((method, url) => (method === "GET" && url === "/api/things/t1" ? { status: 200, body: thing } : { status: 404, body: {} }));
    await show(<ThingForm canEdit={false} />);
    expect(buttons()).not.toContain("Save");
    expect(input("name").disabled).toBe(true);
    expect(view!.container.querySelector('[data-testid="record-read-only"]')!.textContent).toContain("Read only");
    press({ ctrlKey: true, key: "s", code: "KeyS" });
    await settle();
  });

  it("moves to the next and previous record with Alt+PageDown and Alt+PageUp and the toolbar arrows", async () => {
    mockFetch((method, url) => (method === "GET" && url === "/api/things/t1" ? { status: 200, body: thing } : { status: 404, body: {} }));
    const next = vi.fn();
    const previous = vi.fn();
    await show(<ThingForm nav={{ next, previous, position: 2, total: 5 }} />);
    expect(view!.container.textContent).toContain("2 of 5");
    press({ altKey: true, key: "PageDown", code: "PageDown" });
    press({ altKey: true, key: "PageUp", code: "PageUp" });
    expect(next).toHaveBeenCalledOnce();
    expect(previous).toHaveBeenCalledOnce();
  });

  it("prints the record as a PDF in English or Arabic from Alt+R", async () => {
    mockFetch((method, url) => (method === "GET" && url === "/api/things/t1" ? { status: 200, body: thing } : { status: 404, body: {} }));
    await show(<ThingForm />, "ar");
    press({ altKey: true, key: "r", code: "KeyR" });
    await settle();
    const links = [...view!.container.querySelectorAll<HTMLAnchorElement>('[role="menu"] a[role="menuitem"]')];
    expect(links.map((a) => a.textContent)).toEqual(["PDF بالإنجليزية", "PDF بالعربية"]);
    expect(links[1]!.getAttribute("href")).toBe("/api/reports/run/test.thing?thing=t1&format=pdf&language=ar&numerals=latn&disposition=attachment");
    expect(links[0]!.hasAttribute("download")).toBe(true);
    expect(document.activeElement).toBe(links[0]);
  });
});

describe("decimal input", () => {
  it("keeps digits, one dot and a leading minus, turns Arabic-Indic digits into Latin, and respects the scale", () => {
    expect(decimalInput("12.5")).toBe("12.5");
    expect(decimalInput("-0.25")).toBe("-0.25");
    expect(decimalInput("١٢٫٥٠")).toBe("12.50");
    expect(decimalInput("12.505", 2)).toBeNull();
    expect(decimalInput("1e3")).toBeNull();
    expect(decimalInput("1.2.3")).toBeNull();
  });
});
