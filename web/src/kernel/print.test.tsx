import { afterEach, describe, expect, it } from "vitest";
import { render, type Rendered } from "../test/render";
import { PrintDocument } from "./print";

let view: Rendered | undefined;
afterEach(() => view?.unmount());

describe("print layout base", () => {
  it("prints an Arabic document right to left even from an English screen, with its own digits", async () => {
    document.documentElement.dir = "ltr";
    view = await render(
      <PrintDocument
        language="ar"
        numerals="arab"
        title="فاتورة ضريبية"
        issuer="شركة النور للتجارة ذ.م.م"
        facts={[{ labelKey: "shell.print.screen", value: "INV-0042", ltr: true }]}
        printedAt={new Date(Date.UTC(2026, 9, 3, 8, 0))}
        printedBy="مريم"
      >
        <table>
          <tbody>
            <tr>
              <td className="num">١٬٢٣٤٫٥٠</td>
            </tr>
          </tbody>
        </table>
      </PrintDocument>,
    );
    const doc = view.container.querySelector<HTMLElement>(".print-document")!;
    expect(doc.dir).toBe("rtl");
    expect(doc.lang).toBe("ar");
    expect(doc.querySelector("h1")!.textContent).toBe("فاتورة ضريبية");
    expect(doc.querySelector("dt")!.textContent).toBe("الشاشة");
    expect(doc.querySelector("dd")!.getAttribute("dir")).toBe("ltr");
    const footer = doc.querySelector(".print-footer")!.textContent!;
    expect(footer).toContain("طبعه مريم");
    expect(footer).toContain("٢٠٢٦");
  });

  it("prints an English document left to right with Latin digits", async () => {
    view = await render(
      <PrintDocument language="en" title="Tax invoice" issuer="Al Noor Trading LLC" printedAt={new Date(Date.UTC(2026, 9, 3, 8, 0))}>
        <p>Body</p>
      </PrintDocument>,
    );
    const doc = view.container.querySelector<HTMLElement>(".print-document")!;
    expect(doc.dir).toBe("ltr");
    expect(doc.querySelector(".print-footer")!.textContent).toContain("2026");
  });

  it("wraps a live screen: nothing on screen, the letterhead and footer only on paper, no second heading", async () => {
    view = await render(
      <PrintDocument screen language="ar" title="الأدوار" issuer="شركة النور للتجارة ذ.م.م" printedBy="مريم" printedAt={new Date(Date.UTC(2026, 9, 3, 8, 0))}>
        <h1>الأدوار</h1>
      </PrintDocument>,
    );
    const doc = view.container.querySelector<HTMLElement>(".print-document")!;
    expect(doc.classList.contains("print-document-screen")).toBe(true);
    expect(doc.querySelector(".print-letterhead")!.classList.contains("print-only")).toBe(true);
    expect(doc.querySelector(".print-footer")!.classList.contains("print-only")).toBe(true);
    expect(doc.querySelectorAll("h1")).toHaveLength(1);
    expect(doc.querySelector(".print-issuer")!.textContent).toBe("شركة النور للتجارة ذ.م.م");
  });
});
