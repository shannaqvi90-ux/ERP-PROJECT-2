import { act } from "react";
import { afterEach, describe, expect, it } from "vitest";
import { render, settle, type Rendered } from "../../test/render";
import { I18nProvider } from "../i18n";
import { AttachmentsSection } from "./attachments";

const record = "0190a000-0000-7000-8000-0000000000c1";
const base = `/api/attachments/contacts.contacts/${record}/files`;
const fileBase = "/api/attachments/contacts.contacts/files";

const file = (id: string, name: string, over: Record<string, unknown> = {}) => ({
  id, name, contentType: name.endsWith(".png") ? "image/png" : name.endsWith(".pdf") ? "application/pdf" : "text/plain; charset=utf-8", sizeBytes: 2048, sha256: "a".repeat(64),
  previewable: /\.(png|pdf)$/.test(name), createdAt: "2026-11-01T09:30:00Z", createdBy: "u1", createdByName: "Mariam Al Ketbi", createdByNameAr: "مريم الكتبي", ...over,
});

let view: Rendered | undefined;
let items = [file("f1", "Trade licence.pdf"), file("f2", "Seal.png", { sizeBytes: 3 * 1024 * 1024 }), file("f3", "notes.txt", { sizeBytes: 12 })];
let calls: { method: string; url: string; headers: Record<string, string>; body: unknown }[] = [];
let refuse: { status: number; title: string } | null = null;

function install() {
  calls = [];
  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const method = init?.method ?? "GET";
    const url = String(input);
    calls.push({ method, url, headers: (init?.headers ?? {}) as Record<string, string>, body: init?.body });
    if (method === "GET" && url === base) {
      return new Response(JSON.stringify({ items, total: items.length, maxFiles: 100, maxFileBytes: 20 * 1024 * 1024, extensions: ["pdf", "png", "txt"] }), { status: 200, headers: { "Content-Type": "application/json" } });
    }
    if (method === "POST" && url === base) {
      if (refuse) return new Response(JSON.stringify({ title: refuse.title, code: "attachmentsType" }), { status: refuse.status, headers: { "Content-Type": "application/problem+json" } });
      const name = decodeURIComponent((init?.headers as Record<string, string>)["X-File-Name"]!);
      items = [file(`n-${items.length}`, name, { sizeBytes: (init?.body as File).size }), ...items];
      return new Response(JSON.stringify(items[0]), { status: 201, headers: { "Content-Type": "application/json" } });
    }
    if (method === "DELETE") {
      items = items.filter((i) => !url.endsWith(`/${i.id}`));
      return new Response(null, { status: 204 });
    }
    return new Response("{}", { status: 404 });
  }) as typeof fetch;
}

async function show(props: { canRead?: boolean; canWrite?: boolean; language?: "en" | "ar" } = {}) {
  install();
  view = await render(
    <I18nProvider initial={props.language ?? "en"}>
      <AttachmentsSection recordType="contacts.contacts" recordId={record} canRead={props.canRead ?? true} canWrite={props.canWrite ?? true} />
    </I18nProvider>,
  );
  for (let i = 0; i < 3; i++) await settle();
}

afterEach(() => {
  view?.unmount();
  view = undefined;
  refuse = null;
  items = [file("f1", "Trade licence.pdf"), file("f2", "Seal.png", { sizeBytes: 3 * 1024 * 1024 }), file("f3", "notes.txt", { sizeBytes: 12 })];
  document.body.innerHTML = "";
});

const text = () => view!.container.textContent ?? "";
const button = (label: string) => [...document.querySelectorAll<HTMLElement>("button, a.button")].find((b) => b.textContent === label || b.getAttribute("aria-label") === label)!;
const choose = async (...files: File[]) => {
  const input = view!.container.querySelector<HTMLInputElement>('input[type="file"]')!;
  Object.defineProperty(input, "files", { value: files, configurable: true });
  await act(async () => {
    input.dispatchEvent(new Event("change", { bubbles: true }));
  });
  for (let i = 0; i < 4; i++) await settle();
};

describe("the files of a record", () => {
  it("lists the files with their size, who added them and when", async () => {
    await show();
    expect(text()).toContain("3 attachments");
    expect(text()).toContain("Trade licence.pdf");
    expect(text()).toContain("2.0 KB");
    expect(text()).toContain("3.0 MB");
    expect(text()).toContain("12 bytes");
    expect(text()).toContain("Mariam Al Ketbi");
  });

  it("shows the Arabic words, the Arabic name and Arabic plural on an Arabic screen", async () => {
    await show({ language: "ar" });
    expect(text()).toContain("3 مرفقات");
    expect(text()).toContain("مريم الكتبي");
    expect(text()).toContain("إضافة ملفات");
    expect(text()).toContain("كيلوبايت");
  });

  it("shows nothing and asks for nothing without the record's read permission", async () => {
    await show({ canRead: false });
    expect(text()).toBe("");
    expect(calls).toEqual([]);
  });

  it("offers no adding or deleting without the record's write permission", async () => {
    await show({ canWrite: false });
    expect(text()).toContain("Trade licence.pdf");
    expect(view!.container.querySelector('input[type="file"]')).toBeNull();
    expect(button("Add files")).toBeUndefined();
    expect(button("Delete Seal.png")).toBeUndefined();
    expect(button("Preview Seal.png")).toBeDefined();
  });

  it("sends each chosen file on its own with the name, the request header and the bytes", async () => {
    await show();
    await choose(new File(["%PDF-1.4 a"], "A one.pdf", { type: "application/pdf" }), new File(["note"], "b.txt", { type: "text/plain" }));
    const posts = calls.filter((c) => c.method === "POST");
    expect(posts.map((p) => decodeURIComponent(p.headers["X-File-Name"]!)).sort()).toEqual(["A one.pdf", "b.txt"]);
    expect(posts.every((p) => p.headers["X-Erp-Request"] === "1" && p.headers["Content-Type"] === "application/octet-stream")).toBe(true);
    expect(text()).toContain("5 attachments");
    expect(view!.container.querySelector('[role="status"]')!.textContent).toBe("2 files attached");
  });

  it("takes files dropped on the section", async () => {
    await show();
    const section = view!.container.querySelector<HTMLElement>("[data-testid='attachments']")!;
    const dropped = new File(["x"], "dropped.txt");
    const event = new Event("drop", { bubbles: true, cancelable: true });
    Object.defineProperty(event, "dataTransfer", { value: { types: ["Files"], files: [dropped] } });
    await act(async () => {
      section.dispatchEvent(event);
    });
    for (let i = 0; i < 4; i++) await settle();
    expect(event.defaultPrevented).toBe(true);
    expect(calls.some((c) => c.method === "POST" && c.headers["X-File-Name"] === "dropped.txt")).toBe(true);
  });

  it("does not send a file the server would refuse, and says why", async () => {
    await show();
    await choose(new File(["<b>"], "page.html"), new File([""], "empty.txt"), new File([new Uint8Array(21 * 1024 * 1024)], "huge.pdf"));
    expect(calls.filter((c) => c.method === "POST")).toEqual([]);
    expect(text()).toContain("This kind of file is not accepted.");
    expect(text()).toContain("The file is empty.");
    expect(text()).toContain("The file is larger than 20 MB.");
  });

  it("shows the server's reason when it refuses a file", async () => {
    await show();
    refuse = { status: 415, title: "The file's content does not match its kind." };
    await choose(new File(["MZ"], "fake.pdf"));
    expect(view!.container.querySelector('[role="alert"]')!.textContent).toContain("The file's content does not match its kind.");
  });

  it("deletes a file after asking", async () => {
    await show();
    await act(async () => button("Delete notes.txt").click());
    expect(document.querySelector('[role="dialog"]')!.textContent).toContain("notes.txt will be removed");
    await act(async () => [...document.querySelectorAll<HTMLElement>('[role="dialog"] button')].find((b) => b.textContent === "Delete")!.click());
    for (let i = 0; i < 3; i++) await settle();
    expect(calls.some((c) => c.method === "DELETE" && c.url === `${fileBase}/f3`)).toBe(true);
    expect(text()).toContain("2 attachments");
    expect(document.querySelector('[role="dialog"]')).toBeNull();
  });

  it("previews a picture and a PDF in the page and steps between them from the keyboard", async () => {
    await show();
    await act(async () => button("Preview Seal.png").click());
    const dialog = () => document.querySelector<HTMLElement>('[role="dialog"]')!;
    expect(dialog().querySelector("img")!.getAttribute("src")).toBe(`${fileBase}/f2/content?inline=true`);
    await act(async () => {
      dialog().querySelector(".attachments-view")!.dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, key: "ArrowRight" }));
    });
    expect(dialog().querySelector("iframe")!.getAttribute("src")).toBe(`${fileBase}/f1/content?inline=true`);
    // Escape closes it.
    await act(async () => {
      dialog().dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, key: "Escape" }));
    });
    expect(document.querySelector('[role="dialog"]')).toBeNull();
  });

  it("offers a plain file as a download link to its bytes", async () => {
    await show();
    const link = [...view!.container.querySelectorAll<HTMLAnchorElement>("a")].find((a) => a.textContent === "notes.txt")!;
    expect(link.getAttribute("href")).toBe(`${fileBase}/f3/content`);
    expect(link.hasAttribute("download")).toBe(true);
  });
});
