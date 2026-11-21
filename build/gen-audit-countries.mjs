// Generates Resources/countries.json: country names (ISO 3166-1 alpha-2) in English and Arabic from CLDR (node's Intl), the names the screens show.
import { readFileSync, writeFileSync } from "node:fs";
const src = readFileSync(new URL("../web/src/kernel/forms/countries.ts", import.meta.url), "utf8");
const codes = src.match(/"([A-Z ]+)"\s*\+?\s*$/gm).join(" ").replace(/["+]/g, " ").split(/\s+/).filter((c) => /^[A-Z]{2}$/.test(c));
const out = {};
for (const lang of ["en", "ar"]) {
  const names = new Intl.DisplayNames([lang], { type: "region" });
  out[lang] = Object.fromEntries([...new Set(codes)].map((c) => [c, names.of(c)]));
}
writeFileSync(process.argv[2], JSON.stringify(out, null, 0) + "\n");
console.log(Object.keys(out.en).length, out.ar.AE, out.en.AE);
