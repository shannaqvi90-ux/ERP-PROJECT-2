import { describe, expect, it } from "vitest";
import { allowed, collectExtensions, extensions } from "./extensions";

const Noop = () => null;

describe("shell extension points", () => {
  it("gathers every module's contributions in order", () => {
    const all = collectExtensions({
      "../modules/tenancy/extensions.tsx": { extensions: { topbar: [{ key: "tenancy.company", order: 20, component: Noop }] } },
      "../modules/identity/extensions.ts": { extensions: { topbar: [{ key: "identity.x", order: 10, component: Noop }], status: [{ key: "identity.s", component: Noop }] } },
    });
    expect(all.topbar.map((i) => i.key)).toEqual(["identity.x", "tenancy.company"]);
    expect(all.status.map((i) => i.key)).toEqual(["identity.s"]);
  });

  it("keeps each module in its own namespace", () => {
    expect(() =>
      collectExtensions({ "../modules/tenancy/extensions.tsx": { extensions: { topbar: [{ key: "identity.company", component: Noop }] } } }),
    ).toThrow(/must start with "tenancy\."/);
    expect(() =>
      collectExtensions({ "../modules/tenancy/extensions.tsx": { extensions: { status: [{ key: "tenancy.a", component: Noop }, { key: "tenancy.a", component: Noop }] } } }),
    ).toThrow(/twice/);
  });

  it("offers an item only to users whose roles grant its permission", () => {
    const items = [{ key: "a" }, { key: "b", permission: "x.y.read" }];
    expect(allowed(items, () => false).map((i) => i.key)).toEqual(["a"]);
    expect(allowed(items, (p) => p === "x.y.read").map((i) => i.key)).toEqual(["a", "b"]);
  });

  it("finds the modules' real contributions, each guarded by a permission", () => {
    expect(extensions.palette.length).toBeGreaterThan(0);
    for (const source of extensions.palette) expect(source.permission).toBeTruthy();
  });
});
