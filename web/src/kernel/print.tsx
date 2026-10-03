import type { ReactNode } from "react";
import { direction, translate, type Language, type Numerals } from "./i18n";
import { createFormatter } from "./format";

/**
 * The print layout base every printed document builds on (reports in p06 use it).
 *
 * A printed document has its own language, which may differ from the screen's: an Arabic tax
 * invoice can be printed from an English screen. The document sets `lang` and `dir` on its own
 * root, so text, tables and the letterhead mirror for Arabic without the screen changing. Page
 * size, margins and the running page number come from the print stylesheet (styles.css,
 * `@page` and `.print-document`), and the app's chrome (top bar, navigation, status line) is
 * never printed. Numbers and dates use the document language and the given digits.
 *
 * With `screen`, the document wraps a live screen (the shell prints every screen this way): on
 * screen it adds nothing visible, on paper the screen gets the letterhead and the footer.
 */
export type PrintFact = { labelKey: string; value: ReactNode; ltr?: boolean };

export function PrintDocument({
  language,
  numerals = "latn",
  title,
  issuer,
  facts = [],
  printedAt,
  printedBy,
  screen = false,
  children,
}: {
  language: Language;
  numerals?: Numerals;
  /** Already in the document's language. */
  title: string;
  /** The company or workspace issuing the document, in the document's language. */
  issuer: string;
  facts?: PrintFact[];
  printedAt?: Date;
  printedBy?: string;
  /** Wraps a live screen: the letterhead and footer show only on paper. */
  screen?: boolean;
  children: ReactNode;
}) {
  const t = (key: string, params?: Record<string, string | number>) => translate(language, key, params, numerals);
  const format = createFormatter(language, numerals);
  return (
    <article className={screen ? "print-document print-document-screen" : "print-document"} lang={language} dir={direction(language)}>
      <header className={screen ? "print-letterhead print-only print-screen-head" : "print-letterhead"}>
        <div className="print-issuer">{issuer}</div>
        {/* A wrapped screen has its own heading; the letterhead only repeats its name. */}
        {screen ? <div className="print-title">{title}</div> : <h1 className="print-title">{title}</h1>}
      </header>
      {facts.length > 0 && (
        <dl className="print-facts">
          {facts.map((fact) => (
            <div key={fact.labelKey} className="print-fact">
              <dt>{t(fact.labelKey)}</dt>
              <dd dir={fact.ltr ? "ltr" : undefined}>{fact.value}</dd>
            </div>
          ))}
        </dl>
      )}
      <div className="print-body">{children}</div>
      {(printedAt || printedBy) && (
        <footer className={screen ? "print-footer print-only" : "print-footer"}>
          {printedAt && <span>{t("shell.print.printedAt", { time: format.dateTime(printedAt) })}</span>}
          {printedBy && <span>{t("shell.print.printedBy", { name: printedBy })}</span>}
        </footer>
      )}
    </article>
  );
}
