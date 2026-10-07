import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useEffect } from "react";
import { api } from "../api";
import { I18nProvider } from "../i18n";
import { ShortcutProvider, useShortcutList, type ShortcutDef } from "../shortcuts";
import { mockFetch, render, settle, type Rendered } from "../../test/render";
import { everyChord, sweepKeys } from "../../test/keySweep";
import { DateField, DecimalField, MoneyField, SelectField, TextField } from "./fields";
import { clearLeaveGuards } from "./leave";
import { FormSection, FormTabs, RecordForm } from "./RecordForm";
import { useRecordForm } from "./useRecordForm";

// G2 on screen for the record form kernel, by keyboard (critic p06 round 2, plant W2). Every module's
// form is this one, so a key that writes for a read-only user here writes on every screen. A user
// who may not change the record presses every key a keyboard has, alone and with every modifier,
// with focus on the form and on the page, and accepts whatever each key opens: no request other than
// a read leaves, and the shortcut sheet offers none of the form's write keys. The same sweep on an
// editable form does find the save keys (the control: the sweep sees writes when there are some).
// scripts/forms-plant-self-test.mjs plants each such fault in the kernel and requires this file to fail.

let view: Rendered | undefined;

beforeEach(() => {
  localStorage.clear();
  clearLeaveGuards();
  window.history.replaceState(null, "", "/things?open=t1");
});

afterEach(() => {
  view?.unmount();
  view = undefined;
  vi.restoreAllMocks();
});

type Thing = { id: string; name: string; amount: string; currency: string; rate: string; kind: string; due: string; version: number };
type Draft = Omit<Thing, "id" | "version">;
const thing: Thing = { id: "t1", name: "Desk", amount: "12.50", currency: "USD", rate: "3.6725", kind: "a", due: "2026-10-01", version: 3 };

let registered: ShortcutDef[] = [];
function Registered() {
  const list = useShortcutList();
  useEffect(() => {
    registered = list;
  }, [list]);
  return null;
}

/** A form with every kind of field, sections and tabs, the record toolbar (previous and next, print,
 * close) and a record document: everything a module's form can carry. */
function ThingForm({ canEdit, isNew = false, onClose }: { canEdit: boolean; isNew?: boolean; onClose: () => void }) {
  const form = useRecordForm<Thing, Draft>(
    {
      load: isNew ? undefined : (signal) => api<Thing>("GET", "/api/things/t1", undefined, { signal }),
      initial: (t) => ({ name: t?.name ?? "", amount: t?.amount ?? "", currency: t?.currency ?? "AED", rate: t?.rate ?? "", kind: t?.kind ?? "a", due: t?.due ?? "" }),
      canEdit,
      save: (draft, t) => (t ? api<Thing>("PUT", `/api/things/${t.id}`, { ...draft, version: t.version }) : api<Thing>("POST", "/api/things", draft)),
      validate: () => ({}),
    },
    isNew ? "new" : "t1",
  );
  return (
    <RecordForm
      form={form}
      title={form.record?.name ?? "New thing"}
      onClose={onClose}
      nav={{ next: () => undefined, previous: () => undefined, position: 2, total: 3 }}
      document={{ report: "test.thing", parameter: "thing", id: "t1" }}
    >
      <FormSection title="Main">
        <TextField field={form.bind("name")} label="Name" />
        <DecimalField field={form.bind("amount")} label="Quantity" scale={2} />
        <SelectField field={form.bind("kind")} label="Kind" options={[{ value: "a", label: "A" }, { value: "b", label: "B" }]} />
        <DateField field={form.bind("due")} label="Due" />
      </FormSection>
      <FormTabs
        label="More"
        tabs={[
          {
            key: "money",
            label: "Money",
            content: (
              <FormSection title="Price">
                <MoneyField label="Price" amount={form.bind("amount")} currency={form.bind("currency")} rate={form.bind("rate")} baseCurrency="AED" />
              </FormSection>
            ),
          },
          { key: "notes", label: "Notes", content: <FormSection title="Notes"><TextField field={form.bind("name")} label="Note" /></FormSection> },
        ]}
      />
    </RecordForm>
  );
}

async function showForm(canEdit: boolean, isNew = false) {
  view?.unmount();
  let closed = false;
  const calls = mockFetch((method, url) => {
    if (method === "GET" && url === "/api/things/t1") return { status: 200, body: thing };
    if (method === "PUT" || method === "POST") return { status: 200, body: { ...thing, version: thing.version + 1 } };
    return { status: 404, body: {} };
  });
  view = await render(
    <I18nProvider initial="en">
      <ShortcutProvider>
        <Registered />
        <ThingForm canEdit={canEdit} isNew={isNew} onClose={() => (closed = true)} />
      </ShortcutProvider>
    </I18nProvider>,
  );
  await settle();
  await settle();
  return { calls, closed: () => closed };
}

async function sweep(canEdit: boolean, isNew = false) {
  let shownNow = await showForm(canEdit, isNew);
  const container = () => view!.container;
  return sweepKeys({
    calls: shownNow.calls,
    shown: () => !shownNow.closed() && container().querySelector("form.record-form") !== null,
    reopen: async () => {
      shownNow = await showForm(canEdit, isNew);
      return shownNow.calls;
    },
    targets: [
      () => container().querySelector<HTMLElement>(".record-header h2"),
      () => container().querySelector<HTMLElement>('[role="tab"][aria-selected="true"]'),
      () => null,
    ],
  });
}

const writeKeys = ["forms.save", "forms.saveEnter", "forms.discard"];

describe("the record form by keyboard, for a user who may not change the record", () => {
  it("sweeps every key: each letter, digit and named key, alone and with Ctrl, Alt, Shift and their pairs", () => {
    const names = everyChord().map((c) => c.name);
    expect(new Set(names).size).toBe(names.length);
    for (const chord of ["Ctrl+KeyS", "Ctrl+Enter", "Alt+KeyZ", "Alt+KeyN", "Delete", "Ctrl+Shift+KeyD", "Alt+Shift+KeyR", "Escape", "Enter"]) {
      expect(names).toContain(chord);
    }
    expect(names.length).toBeGreaterThanOrEqual(6 * 70);
  });

  it("an existing record: no key sends anything but reads", async () => {
    expect(await sweep(false)).toEqual([]);
  });

  it("a new record the user may not create: no key sends anything", async () => {
    expect(await sweep(false, true)).toEqual([]);
  });

  it("offers none of the form's write keys in the shortcut sheet, while an editable form offers them all", async () => {
    await showForm(false);
    expect(registered.map((s) => s.id).filter((id) => writeKeys.includes(id))).toEqual([]);
    expect(registered.map((s) => s.id)).toContain("forms.print");
    await showForm(true);
    expect(registered.map((s) => s.id).filter((id) => writeKeys.includes(id)).sort()).toEqual([...writeKeys].sort());
  });

  it("the control: the same sweep on an editable form finds the save keys' writes", async () => {
    const found = await sweep(true);
    const keys = new Set(found.filter((w) => w.method === "PUT" && w.url === "/api/things/t1").map((w) => w.key.split(" ")[0]));
    expect(keys).toContain("Ctrl+KeyS");
    expect(keys).toContain("Ctrl+Enter");
  });
});
