# Critic p02 r10 web plants: apply one plant (by name) to the web clone; exits 2 if the text to replace is missing.
import sys, pathlib
root = pathlib.Path('/home/shan/critic/p02-tenancy-r10-web/web/src/modules/tenancy')
P = {
 'W1-new-company-palette-unguarded': ('extensions.tsx', "      available: () => worksInEveryCompany(),\n", ""),
 'W2-every-company-optional-chain': ('records.ts', "  const first = page.items[0];\n  return first !== undefined && first.everyCompany === true;", "  return page.items[0]?.everyCompany !== false;"),
 'W3-new-branch-palette-unguarded': ('extensions.tsx', "      available: async () => branchCompanies(await loadAll<CompanyRow>(\"/api/tenancy/companies\")).length > 0,\n", ""),
 'W4-setting-not-sent': ('CompanyForm.tsx', "        arabicLegalNameRequired: draft.arabicLegalNameRequired,\n", ""),
 'W5-legacy-hint-hidden': ('CompanyForm.tsx', "hint={company?.legacyTaxRegistrationNumber && form.draft.taxRegistrationNumber.trim() === \"\"", "hint={false && company?.legacyTaxRegistrationNumber && form.draft.taxRegistrationNumber.trim() === \"\""),
 'W6-switcher-ignores-server-recent': ('WorkplaceSwitcher.tsx', "[...new Set([...(workplace.recentCompanyIds ?? []), ...recent])]", "[...new Set([...recent])]"),
 'W7-arabic-required-not-marked': ('CompanyForm.tsx', "      required={form.draft.arabicLegalNameRequired} autoFocus", "      autoFocus"),
 'W8-setting-editable-when-read-only': ('CompanyForm.tsx', "<BooleanField field={bind(\"arabicLegalNameRequired\")} label={t(\"tenancy.company.arabicLegalNameRequired\")} />", "<BooleanField field={{ ...bind(\"arabicLegalNameRequired\"), readOnly: false }} label={t(\"tenancy.company.arabicLegalNameRequired\")} />"),
}
name = sys.argv[1]
f, old, new = P[name]
p = root / f
s = p.read_text()
if old not in s:
    print('MISSING', name); sys.exit(2)
p.write_text(s.replace(old, new, 1))
print('applied', name, 'to', f)
