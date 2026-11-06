import type { FieldOption } from "./types";

/** One option per line: "English | Arabic" (the Arabic label is required; a line with only one text is refused). */
export function parseOptions(text: string, existing: readonly FieldOption[]): { options: { value: string | null; labelEn: string; labelAr: string }[]; bad: boolean } {
  const lines = text.split(/\r?\n/).map((l) => l.trim()).filter((l) => l !== "");
  const parsed = lines.map((line) => {
    const [en, ...rest] = line.split("|");
    return { labelEn: (en ?? "").trim(), labelAr: rest.join("|").trim() };
  });
  const bad = parsed.some((p) => p.labelEn === "" || p.labelAr === "");
  // An option keeps the value records already hold: by its English label, else by its place when
  // that place's option has not been matched by a line.
  const used = new Set<string>();
  const byLabel = parsed.map((p) => existing.find((o) => o.labelEn.toLowerCase() === p.labelEn.toLowerCase() && !used.has(o.value)));
  byLabel.forEach((o) => o && used.add(o.value));
  const options = parsed.map((p, i) => {
    const match = byLabel[i] ?? (existing[i] && !used.has(existing[i]!.value) && !byLabel.some((o) => o?.value === existing[i]!.value) ? existing[i] : undefined);
    if (match) used.add(match.value);
    return { value: match?.value ?? null, ...p };
  });
  return { options, bad };
}

/** The options as the lines the form edits. */
export const optionsText = (options: readonly FieldOption[]): string => options.map((o) => `${o.labelEn} | ${o.labelAr}`).join("\n");
