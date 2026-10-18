import { act } from "react";
import { createRoot } from "react-dom/client";
import { describe, expect, it } from "vitest";
import { announceRecordChanged, recordChangedEvent, useRecordChanged } from "./recordEvents";

function Listener({ record, seen }: { record: string; seen: string[] }) {
  useRecordChanged(record, (id) => seen.push(id));
  return null;
}

describe("record changed announcements", () => {
  it("reach the screens listening for that kind of record, with its id, and no other", async () => {
    const users: string[] = [];
    const companies: string[] = [];
    const host = document.createElement("div");
    const root = createRoot(host);
    await act(async () =>
      root.render(
        <>
          <Listener record="identity.user" seen={users} />
          <Listener record="tenancy.company" seen={companies} />
        </>,
      ),
    );
    act(() => announceRecordChanged({ record: "identity.user", id: "u1" }));
    expect(users).toEqual(["u1"]);
    expect(companies).toEqual([]);
    // A malformed event (no detail, no id) is ignored.
    act(() => {
      window.dispatchEvent(new CustomEvent(recordChangedEvent));
      window.dispatchEvent(new CustomEvent(recordChangedEvent, { detail: { record: "identity.user" } }));
    });
    expect(users).toEqual(["u1"]);
    await act(async () => root.unmount());
    // Unmounted screens no longer listen.
    act(() => announceRecordChanged({ record: "identity.user", id: "u2" }));
    expect(users).toEqual(["u1"]);
  });

  it("carry only the record's kind and id", () => {
    const seen: unknown[] = [];
    const listener = (event: Event) => seen.push((event as CustomEvent).detail);
    window.addEventListener(recordChangedEvent, listener);
    announceRecordChanged({ record: "identity.user", id: "u1", extra: "secret" } as never);
    window.removeEventListener(recordChangedEvent, listener);
    expect(seen).toEqual([{ record: "identity.user", id: "u1" }]);
  });
});
